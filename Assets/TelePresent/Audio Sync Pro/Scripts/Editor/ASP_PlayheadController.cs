/*******************************************************
Product - Audio Sync Pro
  Publisher - TelePresent Games
              http://TelePresentGames.dk
  Author    - Martin Hansen
  Created   - 2024
  (c) 2024 Martin Hansen. All rights reserved.
/*******************************************************/

using System;
using UnityEditor;
using UnityEngine;

namespace TelePresent.AudioSyncPro
{

    public class ASP_PlayheadController : IDisposable
    {
        private bool isPlaying = false;
        private AudioSourcePlus audioSourcePlus;
        private ASP_AudioWaveformEditorInput inputHandler;
        private bool isDraggingPlayhead = false;

        public ASP_PlayheadController(AudioSourcePlus source, ASP_AudioWaveformEditorInput inputHandler)
        {
            audioSourcePlus = source;
            this.inputHandler = inputHandler;

            EditorApplication.update += UpdatePlayhead;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            EditorApplication.update -= UpdatePlayhead;
        }

        private void UpdatePlayhead()
        {
            if (!audioSourcePlus)
                return;
            if (audioSourcePlus.audioSource.isPlaying && audioSourcePlus.audioSource.clip)
            {
                audioSourcePlus.playheadPosition = audioSourcePlus.audioSource.time / audioSourcePlus.audioSource.clip.length;
            }
        }

        public bool IsPlaying => isPlaying;

        public void TogglePlayPause()
        {
            if (isPlaying)
            {
                if (audioSourcePlus == null)
                {
                    Debug.LogWarning("AudioSourcePlus is null. Cannot pause audio.");
                    return;
                }
                audioSourcePlus.PauseAudio();
            }
            else
            {
                if (audioSourcePlus == null)
                {
                    Debug.LogWarning("AudioSourcePlus is null. Cannot play audio.");
                    return;
                }
                audioSourcePlus.PlayAudio();
            }
            isPlaying = !isPlaying;
        }

        public void StopAndReset()
        {
            audioSourcePlus.StopAudio();
            audioSourcePlus.playheadPosition = 0f;
            isPlaying = false;
            audioSourcePlus.audioSource.time = 0f;
        }

        public void SetPlayheadToTime(float time)
        {
            if (audioSourcePlus != null && audioSourcePlus.audioSource.clip != null)
            {
                audioSourcePlus.audioSource.time = Mathf.Clamp(time, 0, audioSourcePlus.audioSource.clip.length);
                audioSourcePlus.playheadPosition = audioSourcePlus.audioSource.time / audioSourcePlus.audioSource.clip.length;
            }
        }


        public void DrawPlayhead(Rect waveformRect, float zoomLevel, float viewStart, float viewEnd)
        {
            float visiblePlayheadPosition = (audioSourcePlus.playheadPosition - viewStart) / (viewEnd - viewStart);
            float playheadX = Mathf.Clamp(visiblePlayheadPosition * waveformRect.width, 0, waveformRect.width) + waveformRect.x;

            float arrowWidth = 15f;  // Width of the arrow
            float arrowHeight = 15f; // Height of the arrow
            float tipRadius = 8f;    // Radius of the rounded tip

            Vector3[] arrowVertices = new Vector3[]
            {
            new Vector3(playheadX - arrowWidth / 2, waveformRect.y, 0), // Top-left
            new Vector3(playheadX - tipRadius, waveformRect.y + arrowHeight - tipRadius, 0), // Left of the tip
            new Vector3(playheadX, waveformRect.y + arrowHeight, 0),  // Tip (rounded)
            new Vector3(playheadX + tipRadius, waveformRect.y + arrowHeight - tipRadius, 0), // Right of the tip
            new Vector3(playheadX + arrowWidth / 2, waveformRect.y, 0) // Top-right
            };

            // Determine if the playhead is hovered
            Rect playheadHitbox = new Rect(playheadX - arrowWidth / 2, waveformRect.y, arrowWidth, waveformRect.height);
            bool isHovered = playheadHitbox.Contains(Event.current.mousePosition);

            float opacity = (isHovered || isDraggingPlayhead) ? 1f : 0.5f;
            Color lighterBlue = new Color(0.5f, 0.7f, 1f, opacity);  // Apply transparency

            Handles.color = lighterBlue;
            Handles.DrawAAConvexPolygon(arrowVertices);

            float lineWidth = 2f;  // Thickness of the line
            Rect lineRect = new Rect(playheadX - lineWidth / 2, waveformRect.y + arrowHeight, lineWidth, waveformRect.height - arrowHeight);
            EditorGUI.DrawRect(lineRect, lighterBlue);

            EditorGUIUtility.AddCursorRect(playheadHitbox, MouseCursor.SlideArrow);

            // Handle playhead input
            inputHandler.HandlePlayheadInput(ref isDraggingPlayhead, ref audioSourcePlus.playheadPosition, playheadHitbox, waveformRect, viewStart, viewEnd);
        }
    }
}