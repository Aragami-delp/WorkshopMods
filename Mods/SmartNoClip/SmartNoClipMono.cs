using Kitchen;
using KitchenMods;
using UnityEngine;
using HarmonyLib;
using TMPro;
using System.Linq;
using System;
using Shapes;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using System.Runtime.CompilerServices;

// Namespace should have "Kitchen" in the beginning
namespace KitchenSmartNoClip
{
    public class SmartNoClipMono : MonoBehaviour
    {
        private const string LARGEWALL = "Wall Section(Clone)";
        private const string SHORTWALL = "Short Wall Section(Clone)";
        private const string HATCH = "Hatch Wall Section(Clone)";
        private const string DOOR = "Door Section(Clone)";
        private const string LARGEDOOR = "External Door Section(Clone)";
        private const string APPLIANCE = "Appliance(Clone)";
        private const string FENCE = "Fence Section(Clone)";
        private const string OUTDOORMOVEMENTBLOCKER = "Outdoor Movement Blocker(Clone)";

        private int LAYER_PLAYERS;
        private int LAYER_DEFAULT;

        private bool m_isPrepTime = false;
        private SceneType m_sceneType = SceneType.Null;
        public bool NoclipKeyEnabled = false;

        public float SpeedIncrease = 1f;
        public static SmartNoClipMono Instance { get; private set; }
        public HashSet<Rigidbody> AllMyPlayerRigidbodies = new HashSet<Rigidbody>();

        private void Start()
        {
            if (Instance != null)
            {
                return;
            }
            Instance = this;
            DontDestroyOnLoad(this); // this.gameobject?

            LAYER_PLAYERS = LayerMask.NameToLayer("Players");
            LAYER_DEFAULT = LayerMask.NameToLayer("Default");
        }

        public void ManualNoClipOverride()
        {
            NoclipKeyEnabled ^= true; // Invert
            SetNoClip();
        }

        private static void DisableCollisions(bool ignore, string gameObjectName)
        {
            //SmartNoClip.LogWarning($"DisableCollisions. Ignore: {ignore}; Name: {gameObjectName}");
            Collider[] playerColliders;
            Collider[] targetColliders;
            try
            {
                //TODO: Cache players each frame
                playerColliders = GameObject.FindObjectsOfType<PlayerView>()?.SelectMany(x => x.GetComponents<Collider>()).ToArray(); // All players not just own
                targetColliders = GameObject.FindObjectsOfType<Collider>()?.Where(x => x.gameObject.activeSelf && x.gameObject.name == gameObjectName).ToArray();
            }
            catch (Exception)
            {
                throw; // These shouldn't find a problem, just in case i f something up
            }
            if (playerColliders != null && playerColliders.Length > 0 && targetColliders != null && targetColliders.Length > 0)
            {
                if (gameObjectName == FENCE)
                {
                    float leftFenceLimit = targetColliders.Select(x => x.transform.position.x).Min();
                    float rightFenceLimit = targetColliders.Select(x => x.transform.position.x).Max();
                    float topFenceLimit = targetColliders.Select(x => x.transform.position.z).Max();
                    targetColliders = targetColliders.Where(x => x.transform.position.x != leftFenceLimit && x.transform.position.x != rightFenceLimit && x.transform.position.z != topFenceLimit).ToArray();
                }
                foreach (var item in targetColliders)
                {
                    foreach (var pColl in playerColliders)
                    {
                        Physics.IgnoreCollision(pColl, item, ignore);
                    }
                }
            }
        }

        public void PlayerView_Update_Prefix()
        {
            // Check for any kind of data change and execute noclip update
            if (GameInfo.IsPreparationTime != m_isPrepTime)
            {
                m_isPrepTime = GameInfo.IsPreparationTime;
                CheckOverrideDisable();
                SetNoClip("GameStateChanged");
                return;
            }
            if (GameInfo.CurrentScene != m_sceneType)
            {
                m_sceneType = GameInfo.CurrentScene;
                CheckOverrideDisable();
                SetNoClip("GameStateChanged");
                return;
            }
        }

        public void ApplianceView_SetPosition_Postfix()
        {
            if (NoClipActive)
            {
                DisableCollisions(true, HATCH); // In case a door gets replaced by a hatch
            }
        }

        private void CheckOverrideDisable()
        {
            if (Persistence.Instance["bResetOverrideOnChange"].BoolValue)
            {
                NoclipKeyEnabled = false;
            }
        }

        public static bool NoClipActive
        {
            get
            {
                try
                {
                    return SmartNoClipMono.Instance.NoclipKeyEnabled ^ // Key only as override. Override to the opposite of the currently active XOR
                        (
                           NoClipActive_AllowedInPrep
                        ||
                           NoClipActive_AllowedInDay
                        ||
                           NoClipActive_AllowedInHQ
                        )
                    ;
                }
                catch (Exception e)
                {
                    SmartNoClip.LogError(e.Message + " | " + e.StackTrace);
                    return false;
                }
            }
        }

        #region NoClipActiveRules
        private static bool NoClipActive_AllowedInPrep => GameInfo.IsPreparationTime
                        && GameInfo.CurrentScene == SceneType.Kitchen
                        && Persistence.Instance["bActive_Prep"].BoolValue;

        private static bool NoClipActive_AllowedInDay => !GameInfo.IsPreparationTime
                        && GameInfo.CurrentScene == SceneType.Kitchen
                        && Persistence.Instance["bActive_Day"].BoolValue;

        private static bool NoClipActive_AllowedInHQ => GameInfo.CurrentScene == SceneType.Franchise
                        && Persistence.Instance["bActive_HQ"].BoolValue;
        #endregion

        private void ChangeCollisionMode()
        {
            #region CollisionMode
            // Not sure if its necessary to change back to original, but it's the slightest bit more performant
            if (SmartNoClipMono.NoClipActive)
            {
                foreach (Rigidbody rig in AllMyPlayerRigidbodies)
                {
                    if (rig is not null) // if player still exists
                    {
                        try
                        {
                            rig.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                        }
                        catch (Exception e)
                        {
                            SmartNoClip.LogError(e.InnerException.Message + "\n" + e.StackTrace);
                        }
                    }
                    else
                        SmartNoClip.LogError("No collision change");
                }
            }
            else
            {
                foreach (Rigidbody rig in AllMyPlayerRigidbodies)
                {
                    if (rig is not null) // if player still exists
                    {
                        try
                        {
                            rig.collisionDetectionMode = CollisionDetectionMode.Discrete;
                        }
                        catch (Exception e)
                        {
                            SmartNoClip.LogError(e.InnerException.Message + "\n" + e.StackTrace);
                        }
                        SmartNoClip.LogError("No collision change");
                    }
                }
            }
            #endregion
        }

        public void PostConfigUpdated(string _changedValue)
        {
            Persistence.Instance?.SaveCurrentConfig();
            SetNoClip();
        }

        public void SetNoClip([CallerMemberName] string _callerName = "")
        {
            SmartNoClip.LogInfo($"Noclip set to {NoClipActive} by {_callerName}; Overwrite: {NoclipKeyEnabled}");
            SpeedIncrease = NoClipActive ? Persistence.Instance["fSpeed_Value"].FloatValue : 1f;
            //DisableCollisions(enable, LARGEWALL);
            DisableCollisions(NoClipActive, SHORTWALL);
            DisableCollisions(NoClipActive, HATCH);
            DisableCollisions(NoClipActive, FENCE);
            // Thoses two are disabled by the default layer below
            //DisableCollisions(NoClipActive, DOOR); 
            //DisableCollisions(NoClipActive, LARGEDOOR);

            // Same same but different

            //SmartNoClip.LogWarning("Before: " + Physics.GetIgnoreLayerCollision(LAYER_PLAYERS, LAYER_DEFAULT));
            // Klappt irgendwie nicht mit false (wieder collision aktiv machen), also wird richtig gesetzt aber immer noch keine collision
            Physics.IgnoreLayerCollision(LAYER_PLAYERS, LAYER_DEFAULT, NoClipActive);
            //SmartNoClip.LogWarning("After: " + Physics.GetIgnoreLayerCollision(LAYER_PLAYERS, LAYER_DEFAULT));

            // OutDoorMovementBlocker is layer customers, and can always stay enabled

        }
    }
}
