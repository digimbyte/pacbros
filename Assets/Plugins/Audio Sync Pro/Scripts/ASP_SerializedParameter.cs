/*******************************************************
Product - Audio Sync Pro
  Publisher - TelePresent Games
              http://TelePresentGames.dk
  Author    - Martin Hansen
  Created   - 2024
  (c) 2024 Martin Hansen. All rights reserved.
/*******************************************************/

using System;
using UnityEngine;

namespace TelePresent.AudioSyncPro
{
    [System.Serializable]
    public class ASP_SerializedParameter
    {
        public int intValue;
        public float floatValue;
        public double doubleValue;
        public string stringValue;
        public bool boolValue;
        public UnityEngine.Object objectValue;
        public string enumValue;
        public Vector3 vector3Value;
        public Vector2 vector2Value;
        public Vector4 vector4Value;
        public Vector3Int vector3IntValue;
        public Vector2Int vector2IntValue;
        public Color colorValue;
        public Quaternion quaternionValue;
        public long longValue;
        public uint uintValue;
        public ulong ulongValue;
        public Rect rectValue;
        public RectInt rectIntValue;
        public Bounds boundsValue;
        public BoundsInt boundsIntValue;
        public LayerMask layerMaskValue;
        public AnimationCurve animationCurveValue;
        public Gradient gradientValue;
        public Transform transformValue;

        // Reset all values in the SerializedParameter
        public void ResetValues()
        {
            intValue = 0;
            floatValue = 0f;
            doubleValue = 0.0;
            stringValue = string.Empty;
            boolValue = false;
            objectValue = null;
            enumValue = null;
            vector3Value = Vector3.zero;
            vector2Value = Vector2.zero;
            vector4Value = Vector4.zero;
            vector3IntValue = Vector3Int.zero;
            vector2IntValue = Vector2Int.zero;
            colorValue = Color.white;
            quaternionValue = Quaternion.identity;
            longValue = 0L;
            uintValue = 0u;
            ulongValue = 0ul;
            rectValue = Rect.zero;
            rectIntValue = new RectInt();
            boundsValue = new Bounds();
            boundsIntValue = new BoundsInt();
            layerMaskValue = 0;
            animationCurveValue = new AnimationCurve();
            gradientValue = new Gradient();
            transformValue = null;
        }

        // Retrieve the value based on the parameter type
        public object GetValue(Type parameterType)
        {
            if (parameterType == typeof(int)) return intValue;
            if (parameterType == typeof(float)) return floatValue;
            if (parameterType == typeof(double)) return doubleValue;
            if (parameterType == typeof(string)) return stringValue;
            if (parameterType == typeof(bool)) return boolValue;
            if (typeof(UnityEngine.Object).IsAssignableFrom(parameterType)) return objectValue;
            if (parameterType == typeof(Vector3)) return vector3Value;
            if (parameterType == typeof(Vector2)) return vector2Value;
            if (parameterType == typeof(Vector4)) return vector4Value;
            if (parameterType == typeof(Vector3Int)) return vector3IntValue;
            if (parameterType == typeof(Vector2Int)) return vector2IntValue;
            if (parameterType == typeof(Color)) return colorValue;
            if (parameterType == typeof(Quaternion)) return quaternionValue;
            if (parameterType == typeof(long)) return longValue;
            if (parameterType == typeof(uint)) return uintValue;
            if (parameterType == typeof(ulong)) return ulongValue;
            if (parameterType == typeof(Rect)) return rectValue;
            if (parameterType == typeof(RectInt)) return rectIntValue;
            if (parameterType == typeof(Bounds)) return boundsValue;
            if (parameterType == typeof(BoundsInt)) return boundsIntValue;
            if (parameterType == typeof(LayerMask)) return layerMaskValue;
            if (parameterType == typeof(AnimationCurve)) return animationCurveValue;
            if (parameterType == typeof(Gradient)) return gradientValue;
            if (parameterType == typeof(Transform)) return transformValue;
            if (parameterType.IsEnum) return Enum.Parse(parameterType, enumValue);
            return null;
        }

        // Set the value of the parameter
        public void SetValue(object value)
        {
            ResetValues(); // Reset current values before setting the new one

            if (value is int) intValue = (int)value;
            else if (value is float) floatValue = (float)value;
            else if (value is double) doubleValue = (double)value;
            else if (value is string) stringValue = (string)value;
            else if (value is bool) boolValue = (bool)value;
            else if (value is UnityEngine.Object) objectValue = (UnityEngine.Object)value;
            else if (value is Vector3) vector3Value = (Vector3)value;
            else if (value is Vector2) vector2Value = (Vector2)value;
            else if (value is Vector4) vector4Value = (Vector4)value;
            else if (value is Vector3Int) vector3IntValue = (Vector3Int)value;
            else if (value is Vector2Int) vector2IntValue = (Vector2Int)value;
            else if (value is Color) colorValue = (Color)value;
            else if (value is Quaternion) quaternionValue = (Quaternion)value;
            else if (value is long) longValue = (long)value;
            else if (value is uint) uintValue = (uint)value;
            else if (value is ulong) ulongValue = (ulong)value;
            else if (value is Rect) rectValue = (Rect)value;
            else if (value is RectInt) rectIntValue = (RectInt)value;
            else if (value is Bounds) boundsValue = (Bounds)value;
            else if (value is BoundsInt) boundsIntValue = (BoundsInt)value;
            else if (value is LayerMask) layerMaskValue = (LayerMask)value;
            else if (value is AnimationCurve) animationCurveValue = (AnimationCurve)value;
            else if (value is Gradient) gradientValue = (Gradient)value;
            else if (value is Transform) transformValue = (Transform)value;
            else if (value is Enum) enumValue = value.ToString();
        }
        public ASP_SerializedParameter DeepCopy()
        {
            return new ASP_SerializedParameter
            {
                intValue = this.intValue,
                floatValue = this.floatValue,
                doubleValue = this.doubleValue,
                stringValue = this.stringValue,
                boolValue = this.boolValue,
                objectValue = this.objectValue,
                enumValue = this.enumValue,
                vector3Value = this.vector3Value,
                vector2Value = this.vector2Value,
                vector4Value = this.vector4Value,
                vector3IntValue = this.vector3IntValue,
                vector2IntValue = this.vector2IntValue,
                colorValue = this.colorValue,
                quaternionValue = this.quaternionValue,
                longValue = this.longValue,
                uintValue = this.uintValue,
                ulongValue = this.ulongValue,
                rectValue = this.rectValue,
                rectIntValue = this.rectIntValue,
                boundsValue = this.boundsValue,
                boundsIntValue = this.boundsIntValue,
                layerMaskValue = this.layerMaskValue,
                animationCurveValue = this.animationCurveValue,
                gradientValue = this.gradientValue,
                transformValue = this.transformValue
            };
        }
    }
}