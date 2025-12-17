using UnityEngine;
using System;

namespace TelePresent.AudioSyncPro
{
    [ExecuteInEditMode]
    public class ASP_UniqueID : MonoBehaviour
    {
        [HideInInspector]
        public string ID;

        private void Awake()
        {
            // Generate a unique ID if one doesn't exist
            if (string.IsNullOrEmpty(ID))
            {
                GenerateID();
            }
        }

        private void OnValidate()
        {
            // Ensure the ID is unique and assigned
            if (string.IsNullOrEmpty(ID))
            {
                GenerateID();
            }
        }

        private void GenerateID()
        {
            ID = Guid.NewGuid().ToString();
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
    }
}
