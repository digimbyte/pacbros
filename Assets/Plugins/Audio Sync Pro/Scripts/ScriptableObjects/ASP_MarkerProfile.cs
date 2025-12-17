/*******************************************************
Product - Audio Sync Pro
  Publisher - TelePresent Games
              http://TelePresentGames.dk
  Author    - Martin Hansen
  Created   - 2024
  (c) 2024 Martin Hansen. All rights reserved.
/*******************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TelePresent.AudioSyncPro
{

    [System.Serializable]
    [CreateAssetMenu(fileName = "ASP_MarkerProfile", menuName = "Audio Sync Pro/Marker Profile")]
    public class ASP_MarkerProfile : ScriptableObject
    {
        public AudioClip audioClip;
        public List<ASP_Marker> markerList = new List<ASP_Marker>();

        public void SaveProfile()
        {
            foreach (var marker in markerList)
            {
                foreach (var dynamicPicker in marker.DynamicPickers)
                {
                    if (dynamicPicker.selectedGameObject != null)
                    {
                        AssignID(dynamicPicker.selectedGameObject);
                        ASP_UniqueID uniqueID = dynamicPicker.selectedGameObject.GetComponent<ASP_UniqueID>();
                        dynamicPicker.GameObjectID = uniqueID.ID;
                    }
                    else
                    {
                        dynamicPicker.GameObjectID = null;
                    }
                }
            }
        }


        private void AssignID(GameObject obj)
        {
            if (obj != null)
            {
                ASP_UniqueID uniqueID = obj.GetComponent<ASP_UniqueID>();
                if (uniqueID == null)
                {
                    uniqueID = obj.AddComponent<ASP_UniqueID>();
                    uniqueID.hideFlags = HideFlags.HideInInspector;  // Hide the UniqueID in the inspector
                }

                // Ensure the ID is generated and unique
                if (string.IsNullOrEmpty(uniqueID.ID))
                {
                    uniqueID.ID = Guid.NewGuid().ToString();
#if UNITY_EDITOR
                    UnityEditor.EditorUtility.SetDirty(uniqueID);
#endif
                }
            }
        }

        public void LoadProfile(AudioSourcePlus audioSourcePlus)
        {
            foreach (var marker in markerList)
            {
                marker.RebindReferences();
                audioSourcePlus.markers.Add(marker);
            }
        }
    }

}
