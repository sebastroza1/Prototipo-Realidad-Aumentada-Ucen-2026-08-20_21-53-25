using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Ucen.AR;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.ARSubsystems;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;

namespace Ucen.AR.Editor
{
    public static class ARImageTrackingMvpSetup
    {
        private const string ScenePath = "Assets/Scenes/ARImageTrackingMug.unity";
        private const string MugSourcePrefabPath = "Assets/nappin/OfficeEssentialsPack/Prefabs/(Prb)Mug.prefab";
        private const string MugArPrefabPath = "Assets/AR/Prefabs/MugAR.prefab";
        private const string MarkerTexturePath = "Assets/AR/Images/qr_mug.png";
        private const string ReferenceLibraryPath = "Assets/AR/ReferenceLibraries/QRReferenceImageLibrary.asset";
        private const string MobileRendererPath = "Assets/Settings/Mobile_Renderer.asset";
        private const string TargetName = "qr_mug";
        private const float MarkerPhysicalSizeMeters = 0.1f;

        [MenuItem("Tools/UCEN AR/Setup Image Tracking MVP")]
        public static void SetupImageTrackingMvp()
        {
            EnsureFolders();
            Texture2D markerTexture = EnsureMarkerTexture();
            GameObject mugArPrefab = EnsureMugArPrefab();
            XRReferenceImageLibrary imageLibrary = EnsureReferenceImageLibrary(markerTexture);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "ARImageTrackingMug";

            CreateArSession();
            ARTrackedImageManager trackedImageManager = CreateXrOrigin(imageLibrary, mugArPrefab);
            CreateSceneLighting();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
            bool androidConfigured = ConfigureAndroidAndArCoreSafely();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (trackedImageManager == null)
            {
                throw new InvalidOperationException("No se pudo crear ARTrackedImageManager.");
            }

            string androidStatus = androidConfigured
                ? "ARCore/Android configurado automaticamente."
                : "Revisa Android y XR Management manualmente en Project Settings.";
            Debug.Log($"UCEN AR MVP listo: escena, marcador, reference library y MugAR prefab creados. {androidStatus}");
        }

        [MenuItem("Tools/UCEN AR/Build Debug APK")]
        public static void BuildDebugApk()
        {
            SetupImageTrackingMvp();

            const string buildFolder = "Builds";
            const string apkPath = buildFolder + "/ARImageTrackingMug-debug.apk";
            Directory.CreateDirectory(buildFolder);

            BuildPlayerOptions buildOptions = new BuildPlayerOptions
            {
                scenes = GetEnabledScenePaths(),
                locationPathName = apkPath,
                target = BuildTarget.Android,
                options = BuildOptions.Development
            };

            BuildReport report = BuildPipeline.BuildPlayer(buildOptions);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException($"No se pudo generar el APK. Resultado: {report.summary.result}");
            }

            Debug.Log($"APK debug generado correctamente en {apkPath}");
        }

        private static string[] GetEnabledScenePaths()
        {
            List<string> scenePaths = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && !string.IsNullOrEmpty(scene.path))
                {
                    scenePaths.Add(scene.path);
                }
            }

            return scenePaths.ToArray();
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets/AR");
            EnsureFolder("Assets/AR/Editor");
            EnsureFolder("Assets/AR/Images");
            EnsureFolder("Assets/AR/Prefabs");
            EnsureFolder("Assets/AR/ReferenceLibraries");
            EnsureFolder("Assets/Scenes");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string folderName = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(folderName))
            {
                throw new InvalidOperationException($"Ruta de carpeta invalida: {path}");
            }

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }

        private static Texture2D EnsureMarkerTexture()
        {
            Texture2D existingTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(MarkerTexturePath);
            if (existingTexture != null)
            {
                return existingTexture;
            }

            Texture2D marker = GenerateQrTexture("qr_mug", 18);
            byte[] png = marker.EncodeToPNG();
            File.WriteAllBytes(MarkerTexturePath, png);
            UnityEngine.Object.DestroyImmediate(marker);

            AssetDatabase.ImportAsset(MarkerTexturePath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(MarkerTexturePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.mipmapEnabled = false;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.sRGBTexture = true;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(MarkerTexturePath);
        }

        private static GameObject EnsureMugArPrefab()
        {
            GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MugArPrefabPath);
            if (existingPrefab != null)
            {
                return existingPrefab;
            }

            GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MugSourcePrefabPath);
            if (sourcePrefab == null)
            {
                throw new FileNotFoundException("No se encontro el prefab Mug original del Office Pack.", MugSourcePrefabPath);
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(sourcePrefab) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException("No se pudo instanciar el prefab Mug original.");
            }

            instance.name = "MugAR";
            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(instance, MugArPrefabPath);
            UnityEngine.Object.DestroyImmediate(instance);
            AssetDatabase.ImportAsset(MugArPrefabPath, ImportAssetOptions.ForceUpdate);
            return savedPrefab;
        }

        private static XRReferenceImageLibrary EnsureReferenceImageLibrary(Texture2D markerTexture)
        {
            XRReferenceImageLibrary library = AssetDatabase.LoadAssetAtPath<XRReferenceImageLibrary>(ReferenceLibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<XRReferenceImageLibrary>();
                AssetDatabase.CreateAsset(library, ReferenceLibraryPath);
            }

            for (int i = library.count - 1; i >= 0; i--)
            {
                if (library[i].name == TargetName)
                {
                    library.RemoveAt(i);
                }
            }

            library.Add();
            int index = library.count - 1;
            library.SetName(index, TargetName);
            library.SetTexture(index, markerTexture, true);
            library.SetSpecifySize(index, true);
            library.SetSize(index, new Vector2(MarkerPhysicalSizeMeters, MarkerPhysicalSizeMeters));

            EditorUtility.SetDirty(library);
            return library;
        }

        private static void CreateArSession()
        {
            GameObject sessionObject = new GameObject("AR Session");
            sessionObject.AddComponent<ARSession>();
            sessionObject.AddComponent<ARInputManager>();
        }

        private static ARTrackedImageManager CreateXrOrigin(XRReferenceImageLibrary imageLibrary, GameObject mugArPrefab)
        {
            GameObject originObject = new GameObject("XR Origin");
            XROrigin origin = originObject.AddComponent<XROrigin>();
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;

            GameObject offsetObject = new GameObject("Camera Offset");
            offsetObject.transform.SetParent(originObject.transform, false);

            Camera camera = CreateArCamera(offsetObject.transform);
            origin.CameraFloorOffsetObject = offsetObject;
            origin.Camera = camera;

            ARTrackedImageManager trackedImageManager = originObject.AddComponent<ARTrackedImageManager>();
            trackedImageManager.referenceLibrary = imageLibrary;
            trackedImageManager.requestedMaxNumberOfMovingImages = 1;

            ARImageTrackingController controller = originObject.AddComponent<ARImageTrackingController>();
            ConfigureController(controller, trackedImageManager, mugArPrefab);
            ConfigureCardboardViewMode(originObject, camera);

            return trackedImageManager;
        }

        private static Camera CreateArCamera(Transform parent)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.transform.SetParent(parent, false);
            cameraObject.tag = "MainCamera";

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Color;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 20f;

            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<ARCameraManager>();
            cameraObject.AddComponent<ARCameraBackground>();

            TrackedPoseDriver trackedPoseDriver = cameraObject.AddComponent<TrackedPoseDriver>();
            InputAction positionAction = new InputAction("Position", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3");
            positionAction.AddBinding("<HandheldARInputDevice>/devicePosition");
            InputAction rotationAction = new InputAction("Rotation", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion");
            rotationAction.AddBinding("<HandheldARInputDevice>/deviceRotation");
            trackedPoseDriver.positionInput = new InputActionProperty(positionAction);
            trackedPoseDriver.rotationInput = new InputActionProperty(rotationAction);

            return camera;
        }

        private static void ConfigureController(ARImageTrackingController controller, ARTrackedImageManager trackedImageManager, GameObject mugArPrefab)
        {
            SerializedObject serializedController = new SerializedObject(controller);

            serializedController.FindProperty("trackedImageManager").objectReferenceValue = trackedImageManager;
            serializedController.FindProperty("targetWidthRatio").floatValue = 0.65f;
            serializedController.FindProperty("hoverHeight").floatValue = 0.025f;
            serializedController.FindProperty("showWhenTrackingIsLimited").boolValue = true;
            serializedController.FindProperty("lostTrackingGraceSeconds").floatValue = 0.8f;
            serializedController.FindProperty("floatAmplitude").floatValue = 0.015f;
            serializedController.FindProperty("floatSpeed").floatValue = 1.1f;
            serializedController.FindProperty("rotationSpeed").floatValue = 18f;
            serializedController.FindProperty("tiltAmplitude").floatValue = 2f;
            serializedController.FindProperty("tiltSpeed").floatValue = 0.8f;

            SerializedProperty imageTargets = serializedController.FindProperty("imageTargets");
            imageTargets.arraySize = 1;
            SerializedProperty target = imageTargets.GetArrayElementAtIndex(0);
            target.FindPropertyRelative("imageName").stringValue = TargetName;
            target.FindPropertyRelative("prefab").objectReferenceValue = mugArPrefab;

            serializedController.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controller);
        }

        private static void ConfigureCardboardViewMode(GameObject hostObject, Camera arCamera)
        {
            ARCardboardViewModeController cardboardController = hostObject.GetComponent<ARCardboardViewModeController>();
            if (cardboardController == null)
            {
                cardboardController = hostObject.AddComponent<ARCardboardViewModeController>();
            }

            SerializedObject serializedController = new SerializedObject(cardboardController);
            serializedController.FindProperty("arCamera").objectReferenceValue = arCamera;
            serializedController.FindProperty("startInCardboardMode").boolValue = false;
            serializedController.FindProperty("renderScale").floatValue = 1f;
            serializedController.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(cardboardController);
        }

        private static void CreateSceneLighting()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.75f, 0.75f, 0.75f);

            GameObject lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static void ConfigureAndroidAndArCore()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "cl.ucen.prototipoar");
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.ARCoreEnabled = true;
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });

            EnsureUrpArRendererFeatures();

            XRGeneralSettingsPerBuildTarget buildTargetSettings = GetOrCreateXrSettings();
            if (!buildTargetSettings.HasSettingsForBuildTarget(BuildTargetGroup.Android))
            {
                buildTargetSettings.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
            }

            if (!buildTargetSettings.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
            {
                buildTargetSettings.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            }

            XRGeneralSettings generalSettings = buildTargetSettings.SettingsForBuildTarget(BuildTargetGroup.Android);
            if (generalSettings != null)
            {
                generalSettings.InitManagerOnStart = true;
                EditorUtility.SetDirty(generalSettings);
            }

            XRManagerSettings managerSettings = buildTargetSettings.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            if (managerSettings == null)
            {
                throw new InvalidOperationException("No se pudo crear XRManagerSettings para Android.");
            }

            managerSettings.automaticLoading = true;
            managerSettings.automaticRunning = true;

            bool assigned = XRPackageMetadataStore.AssignLoader(
                managerSettings,
                "UnityEngine.XR.ARCore.ARCoreLoader",
                BuildTargetGroup.Android);

            if (!assigned && !XRPackageMetadataStore.IsLoaderAssigned("UnityEngine.XR.ARCore.ARCoreLoader", BuildTargetGroup.Android))
            {
                throw new InvalidOperationException("No se pudo asignar ARCoreLoader a Android.");
            }

            EditorUtility.SetDirty(managerSettings);
            EditorUtility.SetDirty(buildTargetSettings);
        }

        private static void EnsureUrpArRendererFeatures()
        {
            ScriptableRendererData rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(MobileRendererPath);
            if (rendererData == null)
            {
                Debug.LogWarning($"No se encontro el renderer URP movil en {MobileRendererPath}.");
                return;
            }

            AddRendererFeatureIfMissing<ARBackgroundRendererFeature>(rendererData, "AR Background Renderer Feature");
            AddRendererFeatureIfMissing<ARCommandBufferSupportRendererFeature>(rendererData, "AR Command Buffer Support Renderer Feature");

            SerializedObject serializedRenderer = new SerializedObject(rendererData);
            SerializedProperty nativeRenderPass = serializedRenderer.FindProperty("m_UseNativeRenderPass");
            if (nativeRenderPass != null)
            {
                nativeRenderPass.boolValue = false;
            }

            RebuildRendererFeatureMap(rendererData, serializedRenderer);
            serializedRenderer.ApplyModifiedPropertiesWithoutUndo();

            rendererData.SetDirty();
            EditorUtility.SetDirty(rendererData);
        }

        private static void AddRendererFeatureIfMissing<T>(ScriptableRendererData rendererData, string featureName)
            where T : ScriptableRendererFeature
        {
            foreach (ScriptableRendererFeature feature in rendererData.rendererFeatures)
            {
                if (feature != null && feature.GetType() == typeof(T))
                {
                    feature.SetActive(true);
                    EditorUtility.SetDirty(feature);
                    return;
                }
            }

            T newFeature = ScriptableObject.CreateInstance<T>();
            newFeature.name = featureName;
            newFeature.SetActive(true);

            AssetDatabase.AddObjectToAsset(newFeature, rendererData);
            rendererData.rendererFeatures.Add(newFeature);
            EditorUtility.SetDirty(newFeature);
        }

        private static void RebuildRendererFeatureMap(ScriptableRendererData rendererData, SerializedObject serializedRenderer)
        {
            SerializedProperty features = serializedRenderer.FindProperty("m_RendererFeatures");
            SerializedProperty featureMap = serializedRenderer.FindProperty("m_RendererFeatureMap");
            if (features == null || featureMap == null)
            {
                return;
            }

            features.arraySize = rendererData.rendererFeatures.Count;
            featureMap.arraySize = rendererData.rendererFeatures.Count;

            for (int i = 0; i < rendererData.rendererFeatures.Count; i++)
            {
                ScriptableRendererFeature feature = rendererData.rendererFeatures[i];
                features.GetArrayElementAtIndex(i).objectReferenceValue = feature;

                long localId = 0;
                if (feature != null)
                {
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out localId);
                }

                featureMap.GetArrayElementAtIndex(i).longValue = localId;
            }
        }

        private static bool ConfigureAndroidAndArCoreSafely()
        {
            try
            {
                ConfigureAndroidAndArCore();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"No se pudo completar automaticamente la configuracion Android/ARCore. {exception.Message}");
                return false;
            }
        }

        private static XRGeneralSettingsPerBuildTarget GetOrCreateXrSettings()
        {
            MethodInfo getOrCreateMethod = typeof(XRGeneralSettingsPerBuildTarget).GetMethod(
                "GetOrCreate",
                BindingFlags.NonPublic | BindingFlags.Static);

            if (getOrCreateMethod == null)
            {
                throw new MissingMethodException(nameof(XRGeneralSettingsPerBuildTarget), "GetOrCreate");
            }

            return (XRGeneralSettingsPerBuildTarget)getOrCreateMethod.Invoke(null, null);
        }

        private static void AddSceneToBuildSettings(string scenePath)
        {
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>();
            scenes.Add(new EditorBuildSettingsScene(scenePath, true));

            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.path == scenePath)
                {
                    continue;
                }

                scenes.Add(scene);
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static Texture2D GenerateQrTexture(string payload, int pixelsPerModule)
        {
            bool[,] modules = QrVersionOneGenerator.Generate(payload);
            int moduleCount = modules.GetLength(0);
            int quietZone = 4;
            int textureSize = (moduleCount + quietZone * 2) * pixelsPerModule;

            Texture2D texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
            Color32 white = new Color32(255, 255, 255, 255);
            Color32 black = new Color32(0, 0, 0, 255);

            for (int y = 0; y < textureSize; y++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    int moduleX = x / pixelsPerModule - quietZone;
                    int moduleY = y / pixelsPerModule - quietZone;
                    bool dark = moduleX >= 0
                        && moduleY >= 0
                        && moduleX < moduleCount
                        && moduleY < moduleCount
                        && modules[moduleCount - 1 - moduleY, moduleX];

                    texture.SetPixel(x, y, dark ? black : white);
                }
            }

            texture.Apply(false, false);
            return texture;
        }

        private static class QrVersionOneGenerator
        {
            private const int Size = 21;
            private const int DataCodewordCount = 19;
            private const int ErrorCorrectionCodewordCount = 7;

            public static bool[,] Generate(string payload)
            {
                bool[,] modules = new bool[Size, Size];
                bool[,] reserved = new bool[Size, Size];

                DrawFunctionPatterns(modules, reserved);

                List<byte> data = CreateDataCodewords(payload);
                byte[] errorCorrection = CreateErrorCorrection(data.ToArray(), ErrorCorrectionCodewordCount);
                List<byte> allCodewords = new List<byte>(data);
                allCodewords.AddRange(errorCorrection);

                PlaceData(modules, reserved, allCodewords);
                ApplyMask(modules, reserved);
                DrawFormatBits(modules, reserved, 0);

                return modules;
            }

            private static List<byte> CreateDataCodewords(string payload)
            {
                List<bool> bits = new List<bool>();
                AppendBits(bits, 0b0100, 4);
                AppendBits(bits, payload.Length, 8);

                foreach (char character in payload)
                {
                    AppendBits(bits, character, 8);
                }

                int capacityBits = DataCodewordCount * 8;
                int terminatorLength = Mathf.Min(4, capacityBits - bits.Count);
                AppendBits(bits, 0, terminatorLength);

                while (bits.Count % 8 != 0)
                {
                    bits.Add(false);
                }

                List<byte> codewords = BitsToBytes(bits);
                bool useFirstPad = true;
                while (codewords.Count < DataCodewordCount)
                {
                    codewords.Add(useFirstPad ? (byte)0xEC : (byte)0x11);
                    useFirstPad = !useFirstPad;
                }

                return codewords;
            }

            private static void DrawFunctionPatterns(bool[,] modules, bool[,] reserved)
            {
                DrawFinder(modules, reserved, 0, 0);
                DrawFinder(modules, reserved, Size - 7, 0);
                DrawFinder(modules, reserved, 0, Size - 7);

                for (int i = 8; i < Size - 8; i++)
                {
                    SetFunctionModule(modules, reserved, 6, i, i % 2 == 0);
                    SetFunctionModule(modules, reserved, i, 6, i % 2 == 0);
                }

                SetFunctionModule(modules, reserved, 8, Size - 8, true);

                for (int i = 0; i < 9; i++)
                {
                    Reserve(reserved, 8, i);
                    Reserve(reserved, i, 8);
                    Reserve(reserved, Size - 1 - i, 8);
                    Reserve(reserved, 8, Size - 1 - i);
                }
            }

            private static void DrawFinder(bool[,] modules, bool[,] reserved, int x, int y)
            {
                for (int dy = -1; dy <= 7; dy++)
                {
                    for (int dx = -1; dx <= 7; dx++)
                    {
                        int xx = x + dx;
                        int yy = y + dy;
                        if (xx < 0 || yy < 0 || xx >= Size || yy >= Size)
                        {
                            continue;
                        }

                        bool inFinder = dx >= 0 && dx <= 6 && dy >= 0 && dy <= 6;
                        bool dark = inFinder && (dx == 0 || dx == 6 || dy == 0 || dy == 6 || (dx >= 2 && dx <= 4 && dy >= 2 && dy <= 4));
                        SetFunctionModule(modules, reserved, xx, yy, dark);
                    }
                }
            }

            private static void PlaceData(bool[,] modules, bool[,] reserved, List<byte> codewords)
            {
                List<bool> bits = new List<bool>();
                foreach (byte codeword in codewords)
                {
                    AppendBits(bits, codeword, 8);
                }

                int bitIndex = 0;
                int direction = -1;
                int row = Size - 1;

                for (int col = Size - 1; col > 0; col -= 2)
                {
                    if (col == 6)
                    {
                        col--;
                    }

                    while (true)
                    {
                        for (int c = 0; c < 2; c++)
                        {
                            int x = col - c;
                            if (!reserved[row, x])
                            {
                                modules[row, x] = bitIndex < bits.Count && bits[bitIndex];
                                bitIndex++;
                            }
                        }

                        row += direction;
                        if (row < 0 || row >= Size)
                        {
                            row -= direction;
                            direction = -direction;
                            break;
                        }
                    }
                }
            }

            private static void ApplyMask(bool[,] modules, bool[,] reserved)
            {
                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        if (!reserved[y, x] && ((x + y) % 2 == 0))
                        {
                            modules[y, x] = !modules[y, x];
                        }
                    }
                }
            }

            private static void DrawFormatBits(bool[,] modules, bool[,] reserved, int maskPattern)
            {
                int formatBits = GetFormatBits(maskPattern);

                int[,] first = {
                    {8, 0}, {8, 1}, {8, 2}, {8, 3}, {8, 4}, {8, 5}, {8, 7}, {8, 8},
                    {7, 8}, {5, 8}, {4, 8}, {3, 8}, {2, 8}, {1, 8}, {0, 8}
                };

                int[,] second = {
                    {Size - 1, 8}, {Size - 2, 8}, {Size - 3, 8}, {Size - 4, 8}, {Size - 5, 8}, {Size - 6, 8}, {Size - 7, 8},
                    {8, Size - 8}, {8, Size - 7}, {8, Size - 6}, {8, Size - 5}, {8, Size - 4}, {8, Size - 3}, {8, Size - 2}, {8, Size - 1}
                };

                for (int i = 0; i < 15; i++)
                {
                    bool dark = ((formatBits >> i) & 1) != 0;
                    SetFunctionModule(modules, reserved, first[i, 0], first[i, 1], dark);
                    SetFunctionModule(modules, reserved, second[i, 0], second[i, 1], dark);
                }
            }

            private static int GetFormatBits(int maskPattern)
            {
                int data = (0b01 << 3) | maskPattern;
                int value = data << 10;
                int generator = 0b10100110111;

                for (int i = 14; i >= 10; i--)
                {
                    if (((value >> i) & 1) != 0)
                    {
                        value ^= generator << (i - 10);
                    }
                }

                return ((data << 10) | value) ^ 0b101010000010010;
            }

            private static byte[] CreateErrorCorrection(byte[] data, int degree)
            {
                byte[] generator = CreateGeneratorPolynomial(degree);
                byte[] result = new byte[degree];

                foreach (byte dataByte in data)
                {
                    byte factor = (byte)(dataByte ^ result[0]);
                    Array.Copy(result, 1, result, 0, degree - 1);
                    result[degree - 1] = 0;

                    for (int i = 0; i < degree; i++)
                    {
                        result[i] ^= GfMultiply(generator[i], factor);
                    }
                }

                return result;
            }

            private static byte[] CreateGeneratorPolynomial(int degree)
            {
                List<byte> polynomial = new List<byte> { 1 };
                for (int i = 0; i < degree; i++)
                {
                    List<byte> next = new List<byte>(new byte[polynomial.Count + 1]);
                    byte root = GfPow(2, i);

                    for (int j = 0; j < polynomial.Count; j++)
                    {
                        next[j] ^= GfMultiply(polynomial[j], root);
                        next[j + 1] ^= polynomial[j];
                    }

                    polynomial = next;
                }

                polynomial.RemoveAt(0);
                return polynomial.ToArray();
            }

            private static byte GfPow(byte value, int exponent)
            {
                byte result = 1;
                for (int i = 0; i < exponent; i++)
                {
                    result = GfMultiply(result, value);
                }

                return result;
            }

            private static byte GfMultiply(byte left, byte right)
            {
                int result = 0;
                int a = left;
                int b = right;

                while (b > 0)
                {
                    if ((b & 1) != 0)
                    {
                        result ^= a;
                    }

                    a <<= 1;
                    if ((a & 0x100) != 0)
                    {
                        a ^= 0x11D;
                    }

                    b >>= 1;
                }

                return (byte)result;
            }

            private static void AppendBits(List<bool> bits, int value, int length)
            {
                for (int i = length - 1; i >= 0; i--)
                {
                    bits.Add(((value >> i) & 1) != 0);
                }
            }

            private static List<byte> BitsToBytes(List<bool> bits)
            {
                List<byte> bytes = new List<byte>();
                for (int i = 0; i < bits.Count; i += 8)
                {
                    int value = 0;
                    for (int j = 0; j < 8; j++)
                    {
                        value = (value << 1) | (bits[i + j] ? 1 : 0);
                    }

                    bytes.Add((byte)value);
                }

                return bytes;
            }

            private static void SetFunctionModule(bool[,] modules, bool[,] reserved, int x, int y, bool dark)
            {
                modules[y, x] = dark;
                reserved[y, x] = true;
            }

            private static void Reserve(bool[,] reserved, int x, int y)
            {
                if (x >= 0 && y >= 0 && x < Size && y < Size)
                {
                    reserved[y, x] = true;
                }
            }
        }
    }
}
