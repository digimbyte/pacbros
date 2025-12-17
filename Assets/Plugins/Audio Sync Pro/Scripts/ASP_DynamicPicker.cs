/*******************************************************
Product - Audio Sync Pro
  Publisher - TelePresent Games
              http://TelePresentGames.dk
  Author    - Martin Hansen
  Created   - 2024
  (c) 2024 Martin Hansen. All rights reserved.
/*******************************************************/

using System.Reflection;
using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace TelePresent.AudioSyncPro
{
    [System.Serializable]
    public class ASP_DynamicPicker
    {
        public GameObject selectedGameObject;
        public Component selectedComponent;
        public string selectedMethodName;
        public string selectedComponentName;
        public string GameObjectID;

        public bool IsFoldedOut { get; set; } = true;

        [SerializeField]
        public ASP_SerializedParameter[] methodParameters;

        public void ClearMethodParameters()
        {
            if (methodParameters == null || methodParameters.Length == 0) return;

            foreach (var parameter in methodParameters)
            {
                parameter.ResetValues();
            }
            methodParameters = new ASP_SerializedParameter[0];
        }


        public void InvokeMethod()
        {
            if (string.IsNullOrEmpty(selectedMethodName) || (selectedComponent == null && selectedGameObject == null))
            {
                Debug.LogError("Method name is empty or selected component/GameObject is null. Cannot invoke method.");
                return;
            }

            var target = selectedComponent != null ? (object)selectedComponent : selectedGameObject;
            var method = FindMethod(target);
            if (method == null)
            {
                Debug.LogError($"Method '{selectedMethodName}' not found on target '{target.GetType().Name}' or it is ambiguous.");
                return;
            }

            var parameters = method.GetParameters();
            var values = new object[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                try
                {
                    values[i] = methodParameters[i].GetValue(parameters[i].ParameterType);

                    if (values[i] != null && !parameters[i].ParameterType.IsAssignableFrom(values[i].GetType()))
                    {
                        Debug.LogError($"Type mismatch for parameter {i} in method '{selectedMethodName}'. Expected: {parameters[i].ParameterType}, Got: {values[i].GetType()}");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error converting parameter {i} for method '{selectedMethodName}': {ex.Message}");
                    return;
                }
            }

            try
            {
                if (method.ReturnType == typeof(IEnumerator) || method.ReturnType == typeof(IEnumerable))
                {
                    // Method is a coroutine, use StartCoroutine
                    if (target is MonoBehaviour monoBehaviour)
                    {
                        // Convert IEnumerable to IEnumerator if necessary
                        IEnumerator coroutine = method.ReturnType == typeof(IEnumerable) ? ((IEnumerable)method.Invoke(target, values)).GetEnumerator() : (IEnumerator)method.Invoke(target, values);
                        monoBehaviour.StartCoroutine(coroutine);
                    }
                    else
                    {
                        Debug.LogError("Coroutine methods can only be invoked on MonoBehaviour instances.");
                    }
                }
                else
                {
                    // Method is a regular method, use Invoke
                    method.Invoke(target, values);
                }
            }
            catch (TargetInvocationException ex)
            {
                Debug.LogError($"Error invoking method '{selectedMethodName}': {ex.InnerException?.Message ?? ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Unexpected error invoking method '{selectedMethodName}': {ex.Message}");
            }
        }

        private MethodInfo FindMethod(object target)
        {
            var methods = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
            foreach (var method in methods)
            {
                if (method.Name == selectedMethodName)
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length == methodParameters.Length)
                    {
                        bool isMatch = true;
                        for (int i = 0; i < parameters.Length; i++)
                        {
                            if (!parameters[i].ParameterType.IsAssignableFrom(methodParameters[i].GetValue(parameters[i].ParameterType).GetType()))
                            {
                                isMatch = false;
                                break;
                            }
                        }

                        if (isMatch)
                        {
                            return method;
                        }
                    }
                }
            }
            return null;
        }

        public string GetHeader()
        {
            string gameObjectName = selectedGameObject != null ? selectedGameObject.name : "";
            string methodName = !string.IsNullOrEmpty(selectedMethodName) ? selectedMethodName : "";

            if (selectedGameObject == null)
            {
                return "";
            }
            return $"{gameObjectName} - {methodName}";
        }

        public void ResolveReference()
        {
            if (!string.IsNullOrEmpty(GameObjectID))
            {
                GameObject foundGameObject = FindObjectWithID(GameObjectID);
                if (foundGameObject != null)
                {
                    selectedGameObject = foundGameObject;

                    // Rebind selectedComponent
                    if (!string.IsNullOrEmpty(selectedComponentName))
                    {
                        if (selectedComponentName == "GameObject")
                        {
                            selectedComponent = null;
                        }
                        else
                        {
                            var components = selectedGameObject.GetComponents<Component>();
                            foreach (var component in components)
                            {
                                if (component != null && component.GetType().Name == selectedComponentName)
                                {
                                    selectedComponent = component;
                                    break;
                                }
                            }
                        }
                    }
                }
                else
                {
                    Debug.LogWarning($"GameObject with ID {GameObjectID} not found.");
                }
            }
            else
            {
                selectedGameObject = null;
                selectedComponent = null;
            }
        }



        private GameObject FindObjectWithID(string id)
        {
            var uniqueIDs = Resources.FindObjectsOfTypeAll<ASP_UniqueID>();
            foreach (var uniqueID in uniqueIDs)
            {
                if (uniqueID.ID == id)
                {
                    return uniqueID.gameObject;
                }
            }
            return null;
        }


    }
}