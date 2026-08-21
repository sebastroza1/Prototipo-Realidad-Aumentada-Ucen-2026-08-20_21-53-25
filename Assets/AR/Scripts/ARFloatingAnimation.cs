using UnityEngine;

namespace Ucen.AR
{
    [DisallowMultipleComponent]
    public sealed class ARFloatingAnimation : MonoBehaviour
    {
        [Header("Flotacion")]
        [SerializeField, Min(0f)] private float floatAmplitude = 0.015f;
        [SerializeField, Min(0f)] private float floatSpeed = 1.1f;

        [Header("Rotacion")]
        [SerializeField] private float rotationSpeed = 18f;

        [Header("Inclinacion")]
        [SerializeField, Min(0f)] private float tiltAmplitude = 2f;
        [SerializeField, Min(0f)] private float tiltSpeed = 0.8f;

        private Vector3 baseLocalPosition;
        private Quaternion baseLocalRotation = Quaternion.identity;
        private float elapsedTime;
        private bool isPlaying;

        public void Configure(float amplitude, float speed, float rotationDegreesPerSecond, float tiltDegrees, float tiltCyclesPerSecond)
        {
            floatAmplitude = Mathf.Max(0f, amplitude);
            floatSpeed = Mathf.Max(0f, speed);
            rotationSpeed = rotationDegreesPerSecond;
            tiltAmplitude = Mathf.Max(0f, tiltDegrees);
            tiltSpeed = Mathf.Max(0f, tiltCyclesPerSecond);
        }

        public void SetBasePose(Vector3 localPosition, Quaternion localRotation)
        {
            baseLocalPosition = localPosition;
            baseLocalRotation = localRotation;

            if (!isPlaying)
            {
                ApplyPose(0f);
            }
        }

        public void Play(bool resetTime)
        {
            if (resetTime)
            {
                elapsedTime = 0f;
            }

            isPlaying = true;
            enabled = true;
        }

        public void Pause()
        {
            isPlaying = false;
            enabled = false;
        }

        public void StopAtBasePose()
        {
            isPlaying = false;
            elapsedTime = 0f;
            ApplyPose(0f);
            enabled = false;
        }

        private void OnEnable()
        {
            if (!isPlaying)
            {
                enabled = false;
            }
        }

        private void Update()
        {
            if (!isPlaying)
            {
                return;
            }

            elapsedTime += Time.deltaTime;
            ApplyPose(elapsedTime);
        }

        private void ApplyPose(float time)
        {
            float verticalOffset = Mathf.Sin(time * floatSpeed) * floatAmplitude;
            float rotationY = time * rotationSpeed;
            float tilt = Mathf.Sin(time * tiltSpeed) * tiltAmplitude;

            transform.localPosition = baseLocalPosition + Vector3.up * verticalOffset;
            transform.localRotation = baseLocalRotation * Quaternion.Euler(tilt, rotationY, -tilt * 0.5f);
        }
    }
}
