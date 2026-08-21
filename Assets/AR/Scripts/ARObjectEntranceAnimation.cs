using System.Collections;
using UnityEngine;

namespace Ucen.AR
{
    [DisallowMultipleComponent]
    public sealed class ARObjectEntranceAnimation : MonoBehaviour
    {
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

        private ARFloatingAnimation floatingAnimation;
        private Coroutine animationRoutine;
        private Vector3 targetLocalPosition;
        private Quaternion targetLocalRotation = Quaternion.identity;
        private bool visible;
        private bool entering;
        private bool exiting;

        public bool IsVisibleOrEntering
        {
            get { return visible || entering; }
        }

        public void Configure(
            ARFloatingAnimation floating,
            float showDuration,
            float showStartYOffset,
            AnimationCurve showScaleCurve,
            AnimationCurve showPositionCurve,
            float hideDuration,
            float hideYOffset)
        {
            floatingAnimation = floating;
            entranceDuration = Mathf.Max(0.05f, showDuration);
            entranceStartYOffset = showStartYOffset;
            entranceScaleCurve = showScaleCurve != null ? showScaleCurve : entranceScaleCurve;
            entrancePositionCurve = showPositionCurve != null ? showPositionCurve : entrancePositionCurve;
            exitDuration = Mathf.Max(0.05f, hideDuration);
            exitYOffset = hideYOffset;
        }

        public void SetTargetPose(Vector3 localPosition, Quaternion localRotation)
        {
            targetLocalPosition = localPosition;
            targetLocalRotation = localRotation;

            if (visible && !entering && !exiting)
            {
                transform.localPosition = targetLocalPosition;
                transform.localRotation = targetLocalRotation;
            }
        }

        public void PlayEnter()
        {
            if (entering)
            {
                return;
            }

            StopCurrentRoutine();

            gameObject.SetActive(true);
            floatingAnimation?.StopAtBasePose();
            animationRoutine = StartCoroutine(EnterRoutine());
        }

        public void PlayExit()
        {
            if (!gameObject.activeSelf || exiting || (!visible && !entering))
            {
                return;
            }

            StopCurrentRoutine();
            floatingAnimation?.Pause();
            animationRoutine = StartCoroutine(ExitRoutine());
        }

        public void HideImmediately()
        {
            StopCurrentRoutine();
            floatingAnimation?.StopAtBasePose();
            entering = false;
            exiting = false;
            visible = false;
            transform.localScale = Vector3.zero;
            gameObject.SetActive(false);
        }

        private IEnumerator EnterRoutine()
        {
            entering = true;
            exiting = false;
            visible = false;

            Vector3 startPosition = targetLocalPosition + Vector3.up * entranceStartYOffset;
            transform.localPosition = startPosition;
            transform.localRotation = targetLocalRotation;
            transform.localScale = Vector3.zero;

            float elapsed = 0f;
            while (elapsed < entranceDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / entranceDuration);
                float scale = Mathf.Max(0f, entranceScaleCurve.Evaluate(t));
                float positionT = Mathf.Clamp01(entrancePositionCurve.Evaluate(t));

                transform.localScale = Vector3.one * scale;
                transform.localPosition = Vector3.LerpUnclamped(startPosition, targetLocalPosition, positionT);
                transform.localRotation = targetLocalRotation;

                yield return null;
            }

            transform.localScale = Vector3.one;
            transform.localPosition = targetLocalPosition;
            transform.localRotation = targetLocalRotation;

            entering = false;
            visible = true;
            animationRoutine = null;
            floatingAnimation?.Play(true);
        }

        private IEnumerator ExitRoutine()
        {
            entering = false;
            exiting = true;

            Vector3 startPosition = transform.localPosition;
            Vector3 endPosition = targetLocalPosition + Vector3.up * exitYOffset;
            Vector3 startScale = transform.localScale;

            float elapsed = 0f;
            while (elapsed < exitDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / exitDuration);
                float eased = Mathf.SmoothStep(0f, 1f, t);

                transform.localScale = Vector3.LerpUnclamped(startScale, Vector3.zero, eased);
                transform.localPosition = Vector3.LerpUnclamped(startPosition, endPosition, eased);
                transform.localRotation = targetLocalRotation;

                yield return null;
            }

            transform.localScale = Vector3.zero;
            transform.localPosition = endPosition;
            exiting = false;
            visible = false;
            animationRoutine = null;
            gameObject.SetActive(false);
        }

        private void StopCurrentRoutine()
        {
            if (animationRoutine == null)
            {
                return;
            }

            StopCoroutine(animationRoutine);
            animationRoutine = null;
        }
    }
}
