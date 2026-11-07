using ShootDemo.Core;
using UnityEngine;

namespace ShootDemo.Combat
{
    /// <summary>Scene-owned audio, with bounded enemy feedback and a separate player channel.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatAudio : MonoBehaviour
    {
        [SerializeField] private AudioClip shoot;
        [SerializeField] private AudioClip hit;
        [SerializeField] private AudioClip playerHit;
        [SerializeField] private AudioClip death;
        [SerializeField, Range(0f, 1f)] private float masterVolume = 0.7f;
        private AudioSource shots, impacts, deaths, player;
        private GameStateMachine state;
        private float nextHit, nextDeath;
        public static CombatAudio Instance { get; private set; }

        private AudioSource Channel(int priority)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.priority = priority;
            return source;
        }

        private void Awake()
        {
            Instance = this;
            shots = Channel(100);
            impacts = Channel(180);
            deaths = Channel(160);
            player = Channel(0);
        }

        private void Start()
        {
            state = GameStateMachine.Instance;
            if (state != null) state.StateChanged += OnStateChanged;
        }

        public void Shoot() { if (Playing()) Play(shots, shoot, 0.55f); }

        public void EnemyHit(Vector2 position, bool killed)
        {
            if (!Playing() || !Visible(position)) return;
            float now = Time.unscaledTime;
            if (killed)
            {
                if (now < nextDeath) return;
                nextDeath = now + 0.15f;
                Play(deaths, death, 0.35f);
            }
            else
            {
                if (now < nextHit) return;
                nextHit = now + 0.08f;
                Play(impacts, hit, 0.3f);
            }
        }

        // A fatal hit has already changed the game state; it must still be audible.
        public void PlayerHit(bool killed)
        {
            player.Stop();
            Play(player, killed ? death : playerHit, 1f);
        }

        private bool Playing() => state == null || state.CurrentState == GameState.Playing;

        private static bool Visible(Vector2 position)
        {
            var camera = Camera.main;
            if (camera == null) return true;
            Vector3 point = camera.WorldToViewportPoint(position);
            return point.z > 0f && point.x >= -0.1f && point.x <= 1.1f
                && point.y >= -0.1f && point.y <= 1.1f;
        }

        private void Play(AudioSource source, AudioClip clip, float volume)
        {
            if (clip == null) return;
            // Replace the previous cue on this channel instead of stacking hundreds of voices.
            source.Stop();
            source.clip = clip;
            source.volume = masterVolume * volume;
            source.Play();
        }

        private void OnStateChanged(GameState previous, GameState next)
        {
            foreach (var source in new[] { shots, impacts, deaths, player })
            {
                if (next == GameState.Playing) source.UnPause();
                else if (next == GameState.Paused) source.Pause();
                else source.Stop();
            }
        }

        private void OnDestroy()
        {
            if (state != null) state.StateChanged -= OnStateChanged;
            if (Instance == this) Instance = null;
        }
    }
}
