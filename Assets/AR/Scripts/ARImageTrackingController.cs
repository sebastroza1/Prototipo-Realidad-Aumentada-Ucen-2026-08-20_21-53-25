using System;
using System.Collections.Generic;
using UnityEngine;

#if UCEN_HAS_ARFOUNDATION
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
#endif

namespace Ucen.AR
{
    [DisallowMultipleComponent]
    public sealed class ARImageTrackingController : MonoBehaviour
    {
#if UCEN_HAS_ARFOUNDATION
        [Serializable]
        private sealed class ImagePrefabBinding
        {
            [SerializeField] private string imageName = "qr_mug";
            [SerializeField] private GameObject prefab;

            public string ImageName
            {
                get { return imageName; }
            }

            public GameObject Prefab
            {
                get { return prefab; }
            }
        }

        private sealed class RuntimeInstance
        {
            public Transform PoseRoot;
            public Transform AnimationRoot;
            public Transform ModelRoot;
            public ARObjectEntranceAnimation EntranceAnimation;
            public ARFloatingAnimation FloatingAnimation;
            public Vector3 InitialModelLocalPosition;
            public Quaternion InitialModelLocalRotation;
            public Vector3 InitialModelLocalScale;
            public Vector2 LastImageSize;
            public bool HasValidSize;
            public float LastDetectedTime;
            public readonly List<Renderer> Renderers = new List<Renderer>(8);
        }

        [Header("AR Foundation")]
        [SerializeField] private ARTrackedImageManager trackedImageManager;
        [SerializeField] private Transform contentParent;

        [Header("Targets")]
        [SerializeField] private List<ImagePrefabBinding> imageTargets = new List<ImagePrefabBinding>();

        [Header("Ajuste al marcador")]
        [SerializeField, Range(0.1f, 1f)] private float targetWidthRatio = 0.65f;
        [SerializeField, Min(0f)] private float hoverHeight = 0.025f;
        [SerializeField] private bool showWhenTrackingIsLimited = true;
        [SerializeField, Min(0f)] private float lostTrackingGraceSeconds = 0.8f;

        [Header("Entrada")]
        [SerializeField, Min(0.05f)] private float entranceDuration = 0.85f;
        [SerializeField] private float entranceStartYOffset = -0.025f;
        [SerializeField] private AnimationCurve entranceScaleCurve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.75f, 1.1f),
            new Keyframe(1f, 1f));
        [SerializeField] private AnimationCurve entrancePositionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Salida")]
        [SerializeField, Min(0.05f)] private float exitDuration = 0.3f;
        [SerializeField] private float exitYOffset = -0.02f;

        [Header("Flotacion")]
        [SerializeField, Min(0f)] private float floatAmplitude = 0.015f;
        [SerializeField, Min(0f)] private float floatSpeed = 1.1f;
        [SerializeField] private float rotationSpeed = 18f;
        [SerializeField, Min(0f)] private float tiltAmplitude = 2f;
        [SerializeField, Min(0f)] private float tiltSpeed = 0.8f;

        private readonly Dictionary<string, ImagePrefabBinding> bindingsByImageName = new Dictionary<string, ImagePrefabBinding>(StringComparer.Ordinal);
        private readonly Dictionary<string, RuntimeInstance> instancesByImageName = new Dictionary<string, RuntimeInstance>(StringComparer.Ordinal);
        private readonly Dictionary<TrackableId, string> imageNameByTrackableId = new Dictionary<TrackableId, string>();
        private readonly HashSet<string> warnedMissingTargets = new HashSet<string>(StringComparer.Ordinal);

        private void Reset()
        {
            TryGetComponent(out trackedImageManager);
        }

        private void Awake()
        {
            if (trackedImageManager == null)
            {
                TryGetComponent(out trackedImageManager);
            }

            RebuildBindings();
        }

        private void OnEnable()
        {
            if (trackedImageManager == null)
            {
                Debug.LogError("ARImageTrackingController necesita una referencia a ARTrackedImageManager.", this);
                return;
            }

            RebuildBindings();
            trackedImageManager.trackablesChanged.AddListener(OnTrackablesChanged);
        }

        private void OnDisable()
        {
            if (trackedImageManager != null)
            {
                trackedImageManager.trackablesChanged.RemoveListener(OnTrackablesChanged);
            }
        }

        private void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> eventArgs)
        {
            foreach (ARTrackedImage trackedImage in eventArgs.added)
            {
                UpdateTrackedImage(trackedImage);
            }

            foreach (ARTrackedImage trackedImage in eventArgs.updated)
            {
                UpdateTrackedImage(trackedImage);
            }

            foreach (KeyValuePair<TrackableId, ARTrackedImage> removed in eventArgs.removed)
            {
                HideRemovedImage(removed.Key, removed.Value);
            }
        }

        private void UpdateTrackedImage(ARTrackedImage trackedImage)
        {
            if (trackedImage == null)
            {
                return;
            }

            string imageName = trackedImage.referenceImage.name;
            if (string.IsNullOrEmpty(imageName))
            {
                return;
            }

            imageNameByTrackableId[trackedImage.trackableId] = imageName;

            if (!bindingsByImageName.TryGetValue(imageName, out ImagePrefabBinding binding))
            {
                WarnMissingTargetOnce(imageName);
                return;
            }

            RuntimeInstance instance = GetOrCreateInstance(binding);
            if (instance == null)
            {
                return;
            }

            bool isFullyTracked = trackedImage.trackingState == TrackingState.Tracking;
            bool isDetected = isFullyTracked || (showWhenTrackingIsLimited && trackedImage.trackingState == TrackingState.Limited);
            if (!isDetected)
            {
                if (Time.time - instance.LastDetectedTime >= lostTrackingGraceSeconds)
                {
                    instance.EntranceAnimation.PlayExit();
                }

                return;
            }

            instance.LastDetectedTime = Time.time;
            instance.PoseRoot.SetPositionAndRotation(trackedImage.transform.position, trackedImage.transform.rotation);

            Vector2 imageSize = GetValidImageSize(trackedImage);
            if (ShouldRefit(instance, imageSize))
            {
                FitInstanceToImage(instance, imageSize);
            }

            Vector3 targetLocalPosition = Vector3.up * hoverHeight;
            instance.FloatingAnimation.SetBasePose(targetLocalPosition, Quaternion.identity);
            instance.EntranceAnimation.SetTargetPose(targetLocalPosition, Quaternion.identity);

            if (!instance.EntranceAnimation.IsVisibleOrEntering)
            {
                instance.EntranceAnimation.PlayEnter();
            }
        }

        private void HideRemovedImage(TrackableId trackableId, ARTrackedImage removedImage)
        {
            string imageName = null;
            if (removedImage != null)
            {
                imageName = removedImage.referenceImage.name;
            }

            if (string.IsNullOrEmpty(imageName))
            {
                imageNameByTrackableId.TryGetValue(trackableId, out imageName);
            }

            imageNameByTrackableId.Remove(trackableId);

            if (string.IsNullOrEmpty(imageName))
            {
                return;
            }

            if (instancesByImageName.TryGetValue(imageName, out RuntimeInstance instance))
            {
                instance.EntranceAnimation.PlayExit();
            }
        }

        private RuntimeInstance GetOrCreateInstance(ImagePrefabBinding binding)
        {
            if (binding == null || string.IsNullOrEmpty(binding.ImageName))
            {
                return null;
            }

            if (instancesByImageName.TryGetValue(binding.ImageName, out RuntimeInstance instance))
            {
                return instance;
            }

            if (binding.Prefab == null)
            {
                Debug.LogError($"El target '{binding.ImageName}' no tiene prefab asignado.", this);
                return null;
            }

            Transform parent = contentParent != null ? contentParent : transform;

            GameObject poseRootObject = new GameObject($"AR_{binding.ImageName}");
            poseRootObject.transform.SetParent(parent, false);

            GameObject animationRootObject = new GameObject("AnimatedContent");
            animationRootObject.transform.SetParent(poseRootObject.transform, false);

            ARFloatingAnimation floating = animationRootObject.AddComponent<ARFloatingAnimation>();
            floating.Configure(floatAmplitude, floatSpeed, rotationSpeed, tiltAmplitude, tiltSpeed);
            floating.StopAtBasePose();

            ARObjectEntranceAnimation entrance = animationRootObject.AddComponent<ARObjectEntranceAnimation>();
            entrance.Configure(
                floating,
                entranceDuration,
                entranceStartYOffset,
                entranceScaleCurve,
                entrancePositionCurve,
                exitDuration,
                exitYOffset);

            GameObject modelObject = Instantiate(binding.Prefab, animationRootObject.transform);
            modelObject.name = binding.Prefab.name;

            instance = new RuntimeInstance
            {
                PoseRoot = poseRootObject.transform,
                AnimationRoot = animationRootObject.transform,
                ModelRoot = modelObject.transform,
                EntranceAnimation = entrance,
                FloatingAnimation = floating,
                InitialModelLocalPosition = modelObject.transform.localPosition,
                InitialModelLocalRotation = modelObject.transform.localRotation,
                InitialModelLocalScale = modelObject.transform.localScale
            };

            animationRootObject.SetActive(false);
            instancesByImageName.Add(binding.ImageName, instance);
            return instance;
        }

        private void FitInstanceToImage(RuntimeInstance instance, Vector2 imageSize)
        {
            if (imageSize.x <= 0f)
            {
                Debug.LogWarning("El Image Target no tiene ancho fisico valido. Revisa el tamaño en la XR Reference Image Library.", this);
                return;
            }

            bool wasActive = instance.AnimationRoot.gameObject.activeSelf;
            if (!wasActive)
            {
                instance.AnimationRoot.gameObject.SetActive(true);
            }

            ResetModelTransform(instance);
            RefreshRenderers(instance);

            if (!TryGetLocalRendererBounds(instance.AnimationRoot, instance.Renderers, out Bounds originalBounds))
            {
                Debug.LogWarning("No se encontraron Renderers para calcular el tamano del prefab AR.", this);
                if (!wasActive)
                {
                    instance.AnimationRoot.gameObject.SetActive(false);
                }

                return;
            }

            float modelHorizontalSize = Mathf.Max(originalBounds.size.x, originalBounds.size.z);
            if (modelHorizontalSize <= Mathf.Epsilon)
            {
                Debug.LogWarning("Los bounds horizontales del modelo son demasiado pequenos para calcular escala.", this);
                if (!wasActive)
                {
                    instance.AnimationRoot.gameObject.SetActive(false);
                }

                return;
            }

            float desiredWidth = imageSize.x * targetWidthRatio;
            float scaleFactor = desiredWidth / modelHorizontalSize;
            instance.ModelRoot.localScale = Vector3.Scale(instance.InitialModelLocalScale, Vector3.one * scaleFactor);

            if (!TryGetLocalRendererBounds(instance.AnimationRoot, instance.Renderers, out Bounds scaledBounds))
            {
                return;
            }

            // Centra el modelo en X/Z y apoya su base justo sobre el plano del marcador.
            Vector3 localOffset = new Vector3(-scaledBounds.center.x, -scaledBounds.min.y, -scaledBounds.center.z);
            instance.ModelRoot.localPosition += localOffset;
            instance.LastImageSize = imageSize;
            instance.HasValidSize = true;

            if (!wasActive)
            {
                instance.AnimationRoot.gameObject.SetActive(false);
            }
        }

        private void ResetModelTransform(RuntimeInstance instance)
        {
            instance.AnimationRoot.localScale = Vector3.one;
            instance.AnimationRoot.localRotation = Quaternion.identity;
            instance.AnimationRoot.localPosition = Vector3.zero;

            instance.ModelRoot.localPosition = instance.InitialModelLocalPosition;
            instance.ModelRoot.localRotation = instance.InitialModelLocalRotation;
            instance.ModelRoot.localScale = instance.InitialModelLocalScale;
        }

        private void RefreshRenderers(RuntimeInstance instance)
        {
            instance.Renderers.Clear();
            instance.ModelRoot.GetComponentsInChildren(true, instance.Renderers);
        }

        private static bool TryGetLocalRendererBounds(Transform relativeTo, List<Renderer> renderers, out Bounds localBounds)
        {
            localBounds = default;
            bool hasBounds = false;

            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                EncapsulateWorldBounds(relativeTo, renderer.bounds, ref localBounds, ref hasBounds);
            }

            return hasBounds;
        }

        private static void EncapsulateWorldBounds(Transform relativeTo, Bounds worldBounds, ref Bounds localBounds, ref bool hasBounds)
        {
            Vector3 min = worldBounds.min;
            Vector3 max = worldBounds.max;

            EncapsulateLocalPoint(relativeTo.InverseTransformPoint(new Vector3(min.x, min.y, min.z)), ref localBounds, ref hasBounds);
            EncapsulateLocalPoint(relativeTo.InverseTransformPoint(new Vector3(max.x, min.y, min.z)), ref localBounds, ref hasBounds);
            EncapsulateLocalPoint(relativeTo.InverseTransformPoint(new Vector3(min.x, max.y, min.z)), ref localBounds, ref hasBounds);
            EncapsulateLocalPoint(relativeTo.InverseTransformPoint(new Vector3(min.x, min.y, max.z)), ref localBounds, ref hasBounds);
            EncapsulateLocalPoint(relativeTo.InverseTransformPoint(new Vector3(max.x, max.y, min.z)), ref localBounds, ref hasBounds);
            EncapsulateLocalPoint(relativeTo.InverseTransformPoint(new Vector3(max.x, min.y, max.z)), ref localBounds, ref hasBounds);
            EncapsulateLocalPoint(relativeTo.InverseTransformPoint(new Vector3(min.x, max.y, max.z)), ref localBounds, ref hasBounds);
            EncapsulateLocalPoint(relativeTo.InverseTransformPoint(new Vector3(max.x, max.y, max.z)), ref localBounds, ref hasBounds);
        }

        private static void EncapsulateLocalPoint(Vector3 point, ref Bounds localBounds, ref bool hasBounds)
        {
            if (!hasBounds)
            {
                localBounds = new Bounds(point, Vector3.zero);
                hasBounds = true;
                return;
            }

            localBounds.Encapsulate(point);
        }

        private static bool ShouldRefit(RuntimeInstance instance, Vector2 imageSize)
        {
            if (!instance.HasValidSize)
            {
                return true;
            }

            return Mathf.Abs(instance.LastImageSize.x - imageSize.x) > 0.0001f
                || Mathf.Abs(instance.LastImageSize.y - imageSize.y) > 0.0001f;
        }

        private static Vector2 GetValidImageSize(ARTrackedImage trackedImage)
        {
            if (trackedImage.size.x > 0f && trackedImage.size.y > 0f)
            {
                return trackedImage.size;
            }

            return trackedImage.referenceImage.size;
        }

        private void RebuildBindings()
        {
            bindingsByImageName.Clear();

            for (int i = 0; i < imageTargets.Count; i++)
            {
                ImagePrefabBinding binding = imageTargets[i];
                if (binding == null || string.IsNullOrWhiteSpace(binding.ImageName))
                {
                    continue;
                }

                if (bindingsByImageName.ContainsKey(binding.ImageName))
                {
                    Debug.LogWarning($"El target '{binding.ImageName}' esta duplicado. Se usara la primera configuracion.", this);
                    continue;
                }

                bindingsByImageName.Add(binding.ImageName, binding);
            }
        }

        private void WarnMissingTargetOnce(string imageName)
        {
            if (warnedMissingTargets.Add(imageName))
            {
                Debug.LogWarning($"No hay prefab configurado para el Image Target '{imageName}'.", this);
            }
        }
#else
        private void Awake()
        {
            Debug.LogError("ARImageTrackingController requiere instalar com.unity.xr.arfoundation 6.x. Abre Package Manager o deja que Unity resuelva Packages/manifest.json.", this);
        }
#endif
    }
}
