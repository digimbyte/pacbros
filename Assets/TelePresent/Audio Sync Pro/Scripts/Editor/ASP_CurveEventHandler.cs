/*******************************************************
Product - Audio Sync Pro
  Publisher - TelePresent Games
              http://TelePresentGames.dk
  Author    - Martin Hansen
  Created   - 2024
  (c) 2024 Martin Hansen. All rights reserved.
/*******************************************************/

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace TelePresent.AudioSyncPro
{
    public static class ASP_CurveEventHandler
    {
        public static AnimationCurve selectedCurve = null;
        public static int selectedKeyIndex = -1;
        private const float HitboxRadius = 10.0f;

        public static void HandleCurveEvents(Rect rect, List<AnimationCurve> curves)
        {
            Event evt = Event.current;

            // Ensure that only events within the rect are processed
            if (!rect.Contains(evt.mousePosition))
            {
                return;
            }

            Vector2 localMousePos = evt.mousePosition - new Vector2(rect.x, rect.y);

            switch (evt.type)
            {
                case EventType.MouseDown:
                    if (ASP_CustomCurveEditor.audioWaveformEditor != null)
                        ASP_CustomCurveEditor.audioWaveformEditor.isManipulatingCurves = true;
                    if (evt.clickCount == 2)
                    {
                        HandleDoubleClick(localMousePos, rect, curves);
                    }
                    else
                    {
                        HandleMouseDown(evt, localMousePos, rect, curves);
                    }
                    if (evt.button == 1)
                    {
                        HandleDeleteKey();
                    }
                    break;

                case EventType.MouseDrag:
                    HandleMouseDrag(localMousePos, rect);
                    break;

                case EventType.MouseUp:
                    if (ASP_CustomCurveEditor.audioWaveformEditor != null)
                        ASP_CustomCurveEditor.audioWaveformEditor.isManipulatingCurves = false;
                    evt.Use();
                    break;

                case EventType.MouseMove:
                    HandleMouseMove(localMousePos, rect, curves);
                    break;

                case EventType.KeyDown:
                    if (evt.keyCode == KeyCode.Delete)
                    {
                        HandleDeleteKey();
                    }
                    break;
            }
        }



        private static void HandleMouseDown(Event evt, Vector2 localMousePos, Rect rect, List<AnimationCurve> curves)
        {
            ASP_CustomCurveEditor.audioSourcePlus.audioSource.rolloffMode = AudioRolloffMode.Custom;
            if (rect.Contains(evt.mousePosition))
            {
                if (TrySelectKeyAtMousePosition(localMousePos, rect, curves))
                {
                    evt.Use();
                }
                else
                {
                    // Deselect if clicked outside a key
                    selectedCurve = null;
                    selectedKeyIndex = -1;
                    evt.Use();
                }
            }
        }

        private static void HandleMouseDrag(Vector2 localMousePos, Rect rect)
        {
            if (selectedCurve != null && selectedKeyIndex >= 0 && selectedKeyIndex < selectedCurve.length)
            {
                HandleKeyDrag(localMousePos, rect);
            }
        }

        private static void HandleMouseMove(Vector2 localMousePos, Rect rect, List<AnimationCurve> curves)
        {
            // Highlight the curve under the mouse with a generous hitbox
            if (TrySelectCurveAtMousePosition(localMousePos, rect, curves))
            {
                HandleUtility.Repaint(); // Force repaint to show highlight
            }
        }

        private static void HandleDoubleClick(Vector2 localMousePos, Rect rect, List<AnimationCurve> curves)
        {
            if (TrySelectCurveAtMousePosition(localMousePos, rect, curves))
            {
                if (selectedCurve != null)
                {

                    // Set dirty and record undo for the audio source
                    Undo.RecordObject(ASP_CustomCurveEditor.audioSourcePlus, "Add Keyframe");

                    float normalizedTime = localMousePos.x / rect.width;
                    float normalizedValue = 1.0f - (localMousePos.y / rect.height);

                    Keyframe newKey = new Keyframe(normalizedTime, normalizedValue);
                    selectedKeyIndex = selectedCurve.AddKey(newKey);

                    // Smooth the tangents for the new keyframe
                    if (selectedKeyIndex >= 0 && selectedKeyIndex < selectedCurve.length)
                    {
                        selectedCurve.SmoothTangents(selectedKeyIndex, 0f);
                    }

                    // Ensure the curve has keyframes before assigning it to the audio source
                    if (selectedCurve.length == 0)
                    {
                        selectedCurve.AddKey(new Keyframe(0, 1));
                    }
                    ASP_CustomCurveEditor.audioWaveformEditor.UpdateAudioSourceCurve(RetrieveSelectedCurveInt(selectedCurve));

                    EditorUtility.SetDirty(ASP_CustomCurveEditor.audioSourcePlus);

                    Event.current.Use();
                }
            }
        }


        private static void HandleKeyDrag(Vector2 localMousePos, Rect rect)
        {
            if (selectedCurve == null || selectedKeyIndex < 0 || selectedKeyIndex >= selectedCurve.length)
                return;

            Keyframe key = selectedCurve[selectedKeyIndex];
            float normalizedTime = localMousePos.x / rect.width; // Calculate normalized time based on mouse position
            float normalizedValue = 1.0f - (localMousePos.y / rect.height); // Calculate normalized value based on mouse position

            // Move the keyframe to the new position
            key.time = Mathf.Clamp(normalizedTime, 0f, 1f); // Ensure the time is within 0-1 range
            key.value = Mathf.Clamp(normalizedValue, 0f, 1f); // Ensure the value is within 0-1 range

            // Temporarily remove the key to avoid overwriting issues
            selectedCurve.RemoveKey(selectedKeyIndex);

            // Find a nearby key position to avoid exact overlaps
            float minDistance = 0.001f; // Minimum distance between keys to avoid overlap
            for (int i = 0; i < selectedCurve.length; i++)
            {
                if (Mathf.Abs(selectedCurve[i].time - key.time) < minDistance)
                {
                    key.time = Mathf.Clamp(key.time + minDistance, 0f, 1f); // Offset time slightly to avoid overlap
                }
            }

            // Re-add the key at its new position
            selectedKeyIndex = selectedCurve.AddKey(key);

            // Automatically adjust the tangents of all keys in the curve
            for (int i = 0; i < selectedCurve.length; i++)
            {
                selectedCurve.SmoothTangents(i, 0f); // Smooth tangents for all keys
            }

            // Set dirty and record undo for the audio source
            var audioSource = ASP_CustomCurveEditor.audioSourcePlus?.audioSource;
            if (audioSource != null)
            {
                Undo.RecordObject(audioSource, "Move Keyframe");
                EditorUtility.SetDirty(audioSource);
            }

            Event.current.Use();
        }

        private static bool TrySelectKeyAtMousePosition(Vector2 localMousePos, Rect rect, List<AnimationCurve> curves)
        {
            foreach (var curve in curves)
            {
                for (int i = 0; i < curve.length; i++)
                {
                    Vector2 keyPos = new Vector2(curve[i].time * rect.width, (1.0f - curve[i].value) * rect.height); // Updated: removed division by 500
                    if (Vector2.Distance(localMousePos, keyPos) < HitboxRadius)
                    {
                        selectedCurve = curve;
                        selectedKeyIndex = i;
                        return true;
                    }
                }
            }
            selectedCurve = null;
            selectedKeyIndex = -1;
            return false;
        }

        private static bool TrySelectCurveAtMousePosition(Vector2 localMousePos, Rect rect, List<AnimationCurve> curves)
        {
            foreach (var curve in curves)
            {
                if (IsCursorNearCurve(curve, localMousePos, rect))
                {
                    selectedCurve = curve;
                    return true;
                }
            }
            selectedCurve = null;
            return false;
        }

        private static bool IsCursorNearCurve(AnimationCurve curve, Vector2 mousePos, Rect rect)
        {
            const int segmentCount = 100;
            Vector2 previousPoint = new Vector2(0, rect.height - curve.Evaluate(0) * rect.height);

            for (int i = 1; i <= segmentCount; i++)
            {
                float t = (float)i / segmentCount;
                Vector2 point = new Vector2(t * rect.width, rect.height - (curve.Evaluate(t)) * rect.height); // Updated: removed multiplication by 500

                if (IsPointNearLineSegment(previousPoint, point, mousePos, HitboxRadius * 1.5f)) // Increased hitbox size
                {
                    return true;
                }

                previousPoint = point;
            }
            return false;
        }

        private static bool IsPointNearLineSegment(Vector2 a, Vector2 b, Vector2 point, float radius)
        {
            float dx = b.x - a.x;
            float dy = b.y - a.y;
            float lengthSquared = dx * dx + dy * dy;

            float t = Mathf.Clamp01(((point.x - a.x) * dx + (point.y - a.y) * dy) / lengthSquared);

            float closestX = a.x + t * dx;
            float closestY = a.y + t * dy;

            float distance = Mathf.Sqrt((point.x - closestX) * (point.x - closestX) + (point.y - closestY) * (point.y - closestY));

            return distance < radius;
        }


        private static int RetrieveSelectedCurveInt(AnimationCurve curve)
        {
            foreach (AnimationCurve animaitonCurve in ASP_CustomCurveEditor.audioSourcePlus.audioCurves)
            {
                if (animaitonCurve == curve)
                {
                    return ASP_CustomCurveEditor.audioSourcePlus.audioCurves.IndexOf(animaitonCurve);
                }
            }
            return 0;
        }

        private static void HandleDeleteKey()
        {

            if (selectedCurve != null && selectedKeyIndex >= 0 && selectedCurve.length > 1)
            {
                int currentCurveInt = RetrieveSelectedCurveInt(selectedCurve);

                if (ASP_CustomCurveEditor.audioSourcePlus != null)
                {
                    Undo.RecordObject(ASP_CustomCurveEditor.audioSourcePlus, "Delete Keyframe");
                    selectedCurve.RemoveKey(selectedKeyIndex);
                    ASP_CustomCurveEditor.audioWaveformEditor.UpdateAudioSourceCurve(currentCurveInt);
                    selectedKeyIndex = -1; // Deselect after deletion
                    HandleUtility.Repaint(); // Force a repaint to update the view
                    EditorUtility.SetDirty(ASP_CustomCurveEditor.audioSourcePlus);
                }
            }

        }

    }
}
