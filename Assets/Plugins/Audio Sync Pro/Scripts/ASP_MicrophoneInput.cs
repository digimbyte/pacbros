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


    [RequireComponent(typeof(AudioSource))]
    public class ASP_MicrophoneInput : MonoBehaviour
    {
        private AudioSourcePlus audioSource;

        //void Start()
        //{
        //    // Get the attached AudioSource component
        //    audioSource = GetComponent<AudioSourcePlus>();

        //    // Check if there are any microphones connected
        //    if (Microphone.devices.Length > 0)
        //    {
        //        // Start recording from the first available microphone
        //        string microphoneName = Microphone.devices[0];
        //        audioSource.audioSource.clip = Microphone.Start(microphoneName, true, 1, 48000);

        //        // Wait until the microphone recording starts (usually happens immediately)
        //        while (!(Microphone.GetPosition(microphoneName) > 0)) { }

        //        // Play the audio source, this will play the microphone input
        //        audioSource.audioSource.loop = true;
        //        audioSource.PlayAudio();
        //    }
        //    else
        //    {
        //        Debug.LogWarning("No microphone detected!");
        //    }
        //}

        //void OnDisable()
        //{
        //    // Stop recording when the script is disabled or the game is closed
        //    if (Microphone.IsRecording(null))
        //    {
        //        Microphone.End(null);
        //    }
        //}
    }
}
