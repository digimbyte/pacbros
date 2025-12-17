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

namespace TelePresent.AudioSyncPro
{

    [CustomPropertyDrawer(typeof(ASP_ScaleVector3Attribute))]
    public class ASP_ScaleVector3Drawer : PropertyDrawer
    {
        private bool isChained = false;

        private const float Padding = 5f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            // Begin property field
            EditorGUI.BeginProperty(position, label, property);

            // Calculate rects for each component with padding
            float fieldWidth = (position.width - EditorGUIUtility.labelWidth - 40 - Padding * 3) / 3;

            Rect labelRect = new Rect(position.x, position.y, EditorGUIUtility.labelWidth, position.height);
            Rect buttonRect = new Rect(labelRect.xMax, position.y + 2, 24, 24); // Move button next to label, make it larger
            Rect fieldRectX = new Rect(buttonRect.xMax + Padding, position.y, fieldWidth, position.height);
            Rect fieldRectY = new Rect(fieldRectX.xMax + Padding, position.y, fieldWidth, position.height);
            Rect fieldRectZ = new Rect(fieldRectY.xMax + Padding, position.y, fieldWidth, position.height);

            // Draw the label
            EditorGUI.LabelField(labelRect, label);

            // Draw the fields for the X, Y, Z components with draggable functionality
            SerializedProperty propX = property.FindPropertyRelative("x");
            SerializedProperty propY = property.FindPropertyRelative("y");
            SerializedProperty propZ = property.FindPropertyRelative("z");

            // Store the original values
            float originalX = propX.floatValue;
            float originalY = propY.floatValue;
            float originalZ = propZ.floatValue;

            // Define the labels for the fields
            GUIContent[] labels = { new GUIContent("X"), new GUIContent("Y"), new GUIContent("Z") };
            float[] values = { propX.floatValue, propY.floatValue, propZ.floatValue };

            // Draw the X, Y, and Z fields using MultiFloatField for draggable fields
            EditorGUI.BeginChangeCheck(); // Start listening for changes
            EditorGUI.MultiFloatField(new Rect(fieldRectX.x, position.y, fieldWidth * 3 + Padding * 2, position.height), labels, values);
            if (EditorGUI.EndChangeCheck()) // When a change is detected
            {
                propX.floatValue = values[0];
                propY.floatValue = values[1];
                propZ.floatValue = values[2];
            }

            // If chained, adjust the other values
            if (isChained)
            {
                if (propX.floatValue != originalX)
                {
                    float delta = propX.floatValue - originalX;
                    propY.floatValue += delta;
                    propZ.floatValue += delta;
                }
                else if (propY.floatValue != originalY)
                {
                    float delta = propY.floatValue - originalY;
                    propX.floatValue += delta;
                    propZ.floatValue += delta;
                }
                else if (propZ.floatValue != originalZ)
                {
                    float delta = propZ.floatValue - originalZ;
                    propX.floatValue += delta;
                    propY.floatValue += delta;
                }
            }

            // Draw the chain button without a button box and with a larger icon
            var chainIcon = isChained ? EditorGUIUtility.IconContent("d_Linked") : EditorGUIUtility.IconContent("d_Unlinked");
            chainIcon.tooltip = "Bind Proportions"; // Add tooltip

            // Draw the button and check if it was clicked
            if (GUI.Button(buttonRect, chainIcon, GUIStyle.none))
            {
                isChained = !isChained; // Toggle the state when clicked
            }

            // End property field
            EditorGUI.EndProperty();
        }
    }
}