using UnityEngine;
using ChronoWild.Prototype;
namespace ChronoWild.FirstPerson
{
    public sealed class HandMotion : MonoBehaviour
    {
        public PrototypePlayer player;
        public Transform rightPivot, leftPivot;
        public GameObject leftVisual, rightFallback, runVisual;
        public Animator animator;
        Vector3 rightRest, leftRest;
        void Start() { if (rightPivot) rightRest = rightPivot.localPosition; if (leftPivot) leftRest = leftPivot.localPosition; }
        void Update()
        {
            if (!player) return;
            bool moving = player.IsMoving, running = player.IsRunning;
            if (runVisual) runVisual.SetActive(running);
            if (rightFallback) rightFallback.SetActive(!running);
            if (leftVisual) leftVisual.SetActive(running);
            float wave = moving ? Mathf.Sin(Time.time * (running ? 14 : 8)) * (running ? 0.035f : 0.015f) : 0;
            if (rightPivot) rightPivot.localPosition = rightRest + Vector3.up * wave;
            if (leftPivot) leftPivot.localPosition = leftRest - Vector3.up * wave;
        }
    }
}
