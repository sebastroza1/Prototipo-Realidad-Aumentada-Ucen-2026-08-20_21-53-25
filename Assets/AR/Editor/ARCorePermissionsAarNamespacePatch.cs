using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor.Android;
using UnityEngine;

namespace Ucen.AR.Editor
{
    public sealed class ARCorePermissionsAarNamespacePatch : IPostGenerateGradleAndroidProject
    {
        private const string PermissionsAarName = "unityandroidpermissions.aar";
        private const string IncorrectPackage = "package=\"com.google.ar.core\"";
        private const string CorrectPackage = "package=\"com.example.unityandroidpermissions\"";

        public int callbackOrder => 1000;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string aarPath = Path.Combine(path, "libs", PermissionsAarName);
            if (!File.Exists(aarPath))
            {
                return;
            }

            PatchPermissionsAarManifest(aarPath);
        }

        private static void PatchPermissionsAarManifest(string aarPath)
        {
            string tempAarPath = aarPath + ".patched";
            bool patched = false;

            if (File.Exists(tempAarPath))
            {
                File.Delete(tempAarPath);
            }

            using (ZipArchive source = ZipFile.OpenRead(aarPath))
            using (ZipArchive destination = ZipFile.Open(tempAarPath, ZipArchiveMode.Create))
            {
                foreach (ZipArchiveEntry sourceEntry in source.Entries)
                {
                    ZipArchiveEntry destinationEntry = destination.CreateEntry(
                        sourceEntry.FullName,
                        System.IO.Compression.CompressionLevel.Optimal);
                    destinationEntry.LastWriteTime = sourceEntry.LastWriteTime;

                    using Stream sourceStream = sourceEntry.Open();
                    using Stream destinationStream = destinationEntry.Open();

                    if (string.Equals(sourceEntry.FullName, "AndroidManifest.xml", StringComparison.OrdinalIgnoreCase))
                    {
                        using StreamReader reader = new StreamReader(sourceStream, Encoding.UTF8, true);
                        string manifest = reader.ReadToEnd();
                        string fixedManifest = manifest.Replace(IncorrectPackage, CorrectPackage);

                        byte[] bytes = Encoding.UTF8.GetBytes(fixedManifest);
                        destinationStream.Write(bytes, 0, bytes.Length);
                        patched = patched || !string.Equals(manifest, fixedManifest, StringComparison.Ordinal);
                    }
                    else
                    {
                        sourceStream.CopyTo(destinationStream);
                    }
                }
            }

            if (!patched)
            {
                File.Delete(tempAarPath);
                return;
            }

            File.Copy(tempAarPath, aarPath, true);
            File.Delete(tempAarPath);
            Debug.Log($"Patched {PermissionsAarName} AndroidManifest namespace for Android Gradle manifest merge.");
        }
    }
}
