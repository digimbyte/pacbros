/*******************************************************
Product - Audio Sync Pro
  Publisher - TelePresent Games
              http://TelePresentGames.dk
  Author    - Martin Hansen
  Created   - 2024
  (c) 2024 Martin Hansen. All rights reserved.
/*******************************************************/

using UnityEngine;

namespace TelePresent.AudioSyncPro
{
    public interface ASP_IAudioReaction
    {
        void Initialize(Vector3 initialPosition, Vector3 initialScale, Quaternion initialRotation);
        void React(AudioSourcePlus audioSourcePlus, Transform targetTransform, float rmsValue, float[] spectrumData);
        void ResetToOriginalState(Transform targetTransform);
        bool IsActive { get; set; }

    }
}