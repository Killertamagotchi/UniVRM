﻿using System.Linq;
using UnityEngine;
using UnityEngine.UI;


namespace VRM.FirstPersonSample
{
    public class CanvasManager : MonoBehaviour
    {
        [SerializeField]
        public Button LoadVRMButton;

        [SerializeField]
        public Button LoadBVHButton;

        private void Reset()
        {
            LoadVRMButton = GameObject.FindObjectsByType<Button>(FindObjectsSortMode.InstanceID).FirstOrDefault(x => x.name == "LoadVRM");
            LoadBVHButton = GameObject.FindObjectsByType<Button>(FindObjectsSortMode.InstanceID).FirstOrDefault(x => x.name == "LoadBVH");
        }
    }
}
