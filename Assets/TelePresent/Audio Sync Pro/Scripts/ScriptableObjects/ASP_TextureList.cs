/*******************************************************
Product - Audio Sync Pro
  Publisher - TelePresent Games
              http://TelePresentGames.dk
  Author    - Martin Hansen
  Created   - 2024
  (c) 2024 Martin Hansen. All rights reserved.
/*******************************************************/

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TelePresent.AudioSyncPro
{
    [System.Serializable]

    [CreateAssetMenu(fileName = "ASP_TextureList", menuName = "Audio Sync Pro/Texture List")]
    public class ASP_TextureList : ScriptableObject
    {
        public List<Texture2D> textures = new List<Texture2D>();
    }
}