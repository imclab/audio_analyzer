﻿/* THIS CODE HAS BEEN MODIFIED FROM ITS ORIGINAL
 * WHICH CAN BE FOUND HERE: 
 * https://github.com/keijiro/Reaktion
 */


//
// Reaktion - An audio reactive animation toolkit for Unity.
//
// Copyright (C) 2013, 2014 Keijiro Takahashi
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of
// this software and associated documentation files (the "Software"), to deal in
// the Software without restriction, including without limitation the rights to
// use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
// the Software, and to permit persons to whom the Software is furnished to do so,
// subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
// FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
// COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
// IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
// CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

// An implementation of the state variable filter (SVF)
//
// Originally designed by H. Chamberlin and improved by P. Dutilleux.
// For further details, see the paper by B. Frei.
//
// http://courses.cs.washington.edu/courses/cse490s/11au/Readings/Digital_Sound_Generation_2.pdf

using UnityEngine;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class FilterBands : MonoBehaviour
{
    [SerializeField]
    bool muteAudio = true;

    [SerializeField]
    BandPassFilter[] bands;
    
    void Awake()
    {
        Update();
    }


    void Update()
    {
        for (int i = 0; i < bands.Length; i++)
        {
            bands[i].UpdateBand(); 
        }
    }
        
    
    void OnAudioFilterRead(float[] data, int channels)
    {
        for (int i = 0; i < bands.Length; i++)
        {
            bands[i].ApplyFilter(data, channels, i == bands.Length - 1 && muteAudio); 
        }
    }

    public float GetBandOutput(int band)
    {
        if (band >= bands.Length) band = bands.Length - 1;
        return bands[band].dB;
    }

    [System.Serializable]
    public class BandPassFilter 
    {

        //NOTE: selecting "listen" will cause the filter to be written back into the audio stream
        // this will affect all subsequent filters as long as listen is true
        [SerializeField]
        bool listen;

        //TODO: create property drawer that translates cutoff to cutOffFrequency in editor not in play mode
        [SerializeField]
        [Range(0.0f, 1.0f)]
        float cutoff = 0.5f;

        [SerializeField]
        [Range(1.0f, 10.0f)]
        float q = 1.0f; 

        [SerializeField]
        [Range(0.1f, 10.0f)]
        float bandGain = 1.0f;

        // Cutoff frequency in Hz
        [SerializeField]
        public float cutOffFrequency
        {
            get { return Mathf.Pow(2, 10 * cutoff - 10) * 15000; }
        }

        [SerializeField, HideInInspector]
        protected int order;

        // DSP variables
        float vF, vD, 
            vZ1, vZ2, vZ3;
    
        const float zeroOffset = 1.5849e-13f;
        const float refLevel = 0.70710678118f; // 1/sqrt(2)
        const float minDB = -60.0f;

        float squareSum;
        int sampleCount;


        protected float dbLevel = -60.0f;

        public float dB
        {
            get { return dbLevel; }
        }

        public void UpdateBand()
        {
            var f = 2 / 1.85f * Mathf.Sin(Mathf.PI * cutOffFrequency / AudioSettings.outputSampleRate);
            vD = 1 / q;
            vF = (1.85f - 0.75f * vD * f) * f;
            if (sampleCount < 1) return;

            var rms = Mathf.Min(1.0f, Mathf.Sqrt(squareSum / sampleCount));
            dbLevel = 20.0f * Mathf.Log10(rms / refLevel + zeroOffset);

            squareSum = 0;
            sampleCount = 0;

        }

        public void ApplyFilter(float[] audioData, int channels, bool mute)
        {
            sampleCount += audioData.Length / channels;
            for (var i = 0; i < audioData.Length; i += channels)
            {
                var si = audioData[i];
            
                var _vZ1 = 0.5f * si;
                var _vZ3 = vZ2 * vF + vZ3;
                var _vZ2 = (_vZ1 + vZ1 - _vZ3 - vZ2 * vD) * vF + vZ2;
            
                squareSum += (_vZ2 * _vZ2) * bandGain;
                if(listen)
                {
                    for (int c = 0; c < channels; c++)
                    {
                        audioData[i + c] = _vZ2 * bandGain;
                    }
                }
            
                vZ1 = _vZ1;
                vZ2 = _vZ2;
                vZ3 = _vZ3;
            }
            if(mute)
            {
                for (int i = 0; i < audioData.Length; i++)
                {
                    audioData[i] = 0;
                }
            }
        }
    }

}




#if UNITY_EDITOR

[CustomEditor(typeof(FilterBands))]
class FilterBandsEditor : Editor
{

    SerializedProperty bands;
    SerializedProperty mute;

    Color bgColor;

    FilterBands obj;
    SerializedObject tObj;
    
    private void OnEnable()
    {
        obj = target as FilterBands;    
        bands = serializedObject.FindProperty("bands");
        mute = serializedObject.FindProperty("muteAudio");
        bgColor = GUI.backgroundColor;
    }

    public override void OnInspectorGUI()
    {
        //tObj.Update();
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.PropertyField(mute);
        int bandCount = bands.arraySize;

        GUILayout.BeginHorizontal();
        GUI.backgroundColor = Color.green;
        if(GUILayout.Button("add band", GUILayout.Width(80), GUILayout.ExpandWidth(true)))
        {
            bands.InsertArrayElementAtIndex(bands.arraySize);
            bands.GetArrayElementAtIndex(bands.arraySize - 1).FindPropertyRelative("order").intValue = bands.arraySize - 1;
        }
        GUI.backgroundColor = Color.red;
        if(GUILayout.Button("remove band", GUILayout.Width(80), GUILayout.ExpandWidth(true)))
        {
            bands.DeleteArrayElementAtIndex(bands.arraySize - 1);
        }
        GUILayout.EndHorizontal();

        GUI.backgroundColor = bgColor;
        // make sure that bands get displayed in the correct order - arrays are not guaranteed to be saved in order
        int[] order = new int[bands.arraySize];
        for (int i = 0; i < bands.arraySize; i++)
        {
            order[i] = bands.GetArrayElementAtIndex(i).FindPropertyRelative("order").intValue;
        }

        for (int i = 0; i < bands.arraySize; i++)
        {
            SerializedProperty band = bands.GetArrayElementAtIndex(order[i]);
            SerializedProperty listen = band.FindPropertyRelative("listen");
            SerializedProperty cutoff = band.FindPropertyRelative("cutoff");
            SerializedProperty q = band.FindPropertyRelative("q");
            SerializedProperty gain = band.FindPropertyRelative("bandGain");
            SerializedProperty hz = band.FindPropertyRelative("cutoff");

            GUI.backgroundColor = Color.yellow;
            EditorGUILayout.PropertyField(listen);

            GUI.backgroundColor = Color.cyan;
            GUILayout.BeginHorizontal();
            GUILayout.Label("cutoff in hz: ", GUILayout.Width(80));
            EditorGUILayout.FloatField(Mathf.Pow(2, 10 * hz.floatValue - 10) * 15000);
            GUILayout.EndHorizontal();

            GUI.backgroundColor = Color.magenta;
            EditorGUILayout.PropertyField(cutoff);
            EditorGUILayout.PropertyField(q);
            EditorGUILayout.PropertyField(gain);

            if(i < bands.arraySize - 1) GUILayout.Space(40);
        }
        
        serializedObject.ApplyModifiedProperties();
    }
}
#endif