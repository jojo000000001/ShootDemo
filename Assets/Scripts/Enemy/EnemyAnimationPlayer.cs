using UnityEngine;

namespace ShootDemo.Enemy
{
    internal enum EnemyAnimation { Idle, Run, AttackPre, AttackDo, Walk }

    /// <summary>Leaves pose evaluation to Unity's animation phase after selecting a state.</summary>
    internal sealed class EnemyAnimationPlayer
    {
        private readonly Animator animator;
        private readonly int[] hashes;
        private readonly float[] lengths = { -1f, -1f, -1f, -1f, -1f };
        private RuntimeAnimatorController controller;
        private int currentHash;

        public EnemyAnimationPlayer(Animator animator, string prefix)
        {
            this.animator = animator;
            string path = "Base Layer." + prefix + "_";
            hashes = new[] { Animator.StringToHash(path + "idle"), Animator.StringToHash(path + "run"),
                Animator.StringToHash(path + "attack_pre"), Animator.StringToHash(path + "attack_do"),
                Animator.StringToHash(path + "walk") };
        }

        public void Play(EnemyAnimation state, float duration = 0f)
        {
            if (animator == null) return;
            if (controller != animator.runtimeAnimatorController)
            {
                controller = animator.runtimeAnimatorController;
                Reset();
                for (int i = 0; i < lengths.Length; i++) lengths[i] = -1f;
            }
            int index = (int)state;
            int hash = hashes[index];
            if (hash == currentHash || !animator.HasState(0, hash)) return;
            currentHash = hash;
            animator.speed = 1f;
            animator.Play(hash, 0, 0f);
            if (duration <= 0f) return;
            if (lengths[index] < 0f)
            {
                // Read the actual controller state once, including state speed/override clips.
                // Repeated Update(0) forces animation jobs to synchronize on every attack.
                // The first attack may happen off camera. CullCompletely must not leave
                // us reading the previous state's length and caching an incorrect speed.
                var culling = animator.cullingMode;
                try
                {
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.Update(0f);
                    lengths[index] = animator.GetCurrentAnimatorStateInfo(0).length;
                }
                finally { animator.cullingMode = culling; }
            }
            animator.speed = lengths[index] / duration;
        }

        public void Reset() { currentHash = 0; }
    }
}
