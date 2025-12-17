/*******************************************************
Product - Audio Sync Pro
  Publisher - TelePresent Games
              http://TelePresentGames.dk
  Author    - Martin Hansen
  Created   - 2024
  (c) 2024 Martin Hansen. All rights reserved.
/*******************************************************/

using System.Collections.Generic;
using UnityEngine;

namespace TelePresent.AudioSyncPro
{
    [System.Serializable]
    public class ASP_Marker
    {
        public float Time;
        public float normalizedTimelinePosition;
        public bool IsSelected;
        public bool IsTriggered = false;
        public string MarkerName = "Marker Name (Click to Rename)";
        public bool IsEffectActive { get; set; }
        public bool ExecuteInEditMode = false;
        public float EffectStartTime { get; private set; }
        public float EffectDuration = 1f;
        public bool justTriggered;
        public Color justTriggeredColor = new Color(.5f, 2, .5f);
        public AudioSourcePlus AudioSourcePlusReference;

        [SerializeField]
        public List<ASP_DynamicPicker> DynamicPickers = new List<ASP_DynamicPicker>();

        public ASP_Marker(AudioSourcePlus audioSourcePlus)
        {
            AudioSourcePlusReference = audioSourcePlus;
        }

        public void Trigger()
        {
            IsTriggered = true;

            foreach (var dynamicPicker in DynamicPickers)
            {
                dynamicPicker.InvokeMethod();
            }
        }

        public ASP_Marker DeepCopy()
        {
            ASP_Marker newMarker = new ASP_Marker(AudioSourcePlusReference)
            {
                Time = this.Time,
                normalizedTimelinePosition = this.normalizedTimelinePosition,
                IsSelected = this.IsSelected,
                IsTriggered = this.IsTriggered,
                MarkerName = this.MarkerName,
                IsEffectActive = this.IsEffectActive,
                EffectDuration = this.EffectDuration,
                justTriggered = this.justTriggered,
                justTriggeredColor = this.justTriggeredColor
            };

            newMarker.DynamicPickers = new List<ASP_DynamicPicker>();
            foreach (var picker in this.DynamicPickers)
            {
                var newPicker = new ASP_DynamicPicker
                {
                    selectedGameObject = picker.selectedGameObject,
                    selectedComponent = picker.selectedComponent,
                    selectedMethodName = picker.selectedMethodName,
                    selectedComponentName = picker.selectedComponentName,
                };

                if (picker.methodParameters != null)
                {
                    newPicker.methodParameters = new ASP_SerializedParameter[picker.methodParameters.Length];
                    for (int i = 0; i < picker.methodParameters.Length; i++)
                    {
                        newPicker.methodParameters[i] = new ASP_SerializedParameter();
                        newPicker.methodParameters[i].SetValue(picker.methodParameters[i].GetValue(picker.methodParameters[i].GetType()));
                    }
                }

                newMarker.DynamicPickers.Add(newPicker);
            }

            return newMarker;
        }

        public void RebindReferences()
        {
            foreach (var dynamicPicker in DynamicPickers)
            {
                dynamicPicker.ResolveReference();
            }
        }


        public void MarkerBorn()
        {
            DynamicPickers.Add(new ASP_DynamicPicker());
        }

        public void ResetTrigger()
        {
            IsTriggered = false;
        }
    }
}
