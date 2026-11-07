using ShootDemo.Core;
using ShootDemo.Player;
using UnityEngine;
using UnityEngine.UI;

namespace ShootDemo.UI
{
    /// <summary>Only updates widgets when their source values change.</summary>
    public sealed class PlayerHud : MonoBehaviour
    {
        [SerializeField] private PlayerHealth player;
        [SerializeField] private PlayerDash dash;
        [SerializeField] private MatchSession session;
        [SerializeField] private GameStateMachine state;
        [SerializeField] private GameObject content;
        [SerializeField] private Image healthFill;
        [SerializeField] private Image dashFill;
        [SerializeField] private Text healthText;
        [SerializeField] private Text dashText;
        [SerializeField] private Text killText;
        [SerializeField] private Text waveText;
        [SerializeField] private Text resultTitle;
        [SerializeField] private Text resultKills;
        private bool started;
        private bool bound;

        private void Start()
        {
            started = true;
            BindSources();
        }

        private void OnEnable()
        {
            if (started) BindSources();
        }

        private void BindSources()
        {
            if (bound) return;
            if (state == null) state = GameStateMachine.Instance;
            if (session == null) session = FindObjectOfType<MatchSession>();
            if (player == null) player = FindObjectOfType<PlayerHealth>();
            if (dash == null && player != null) dash = player.GetComponent<PlayerDash>();
            if (player == null || dash == null || session == null || state == null)
            {
                Debug.LogError("PlayerHud needs player, dash, session and state bindings.", this);
                return;
            }
            bound = true;
            player.HealthChanged += OnHealth;
            dash.CooldownChanged += OnCooldown;
            session.Changed += OnSession;
            state.StateChanged += OnState;
            OnHealth(player.CurrentHealth, player.MaxHealth);
            OnCooldown(dash.CooldownRemaining, dash.CooldownDuration);
            OnSession();
            OnState(state.CurrentState, state.CurrentState);
        }

        private void OnHealth(float current, float maximum)
        {
            SetBar(healthFill, maximum > 0f ? current / maximum : 0f);

            if (healthText != null)
            {
                healthText.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(maximum)}";
            }
        }

        private void OnCooldown(float remaining, float total)
        {
            SetBar(dashFill, total > 0f ? 1f - remaining / total : 1f);

            if (dashText != null)
            {
                dashText.text = remaining > 0f ? $"闪避 {remaining:F1}s" : "闪避 就绪 [空格]";
            }
        }

        private static void ShowCount(Text label, int count)
        {
            if (label == null)
            {
                return;
            }

            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.text = count.ToString();
        }

        private static void SetBar(Image fill, float amount)
        {
            if (fill == null)
            {
                return;
            }

            amount = Mathf.Clamp01(amount);
            fill.type = Image.Type.Sliced;
            RectTransform rect = fill.rectTransform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(amount, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void OnSession()
        {
            ShowCount(killText, session.DefeatedCount);
            ShowCount(resultKills, session.DefeatedCount);

            if (waveText != null)
            {
                waveText.text = session.IsStressMode ? "压力测试" : $"击杀 {session.DefeatedCount} / {MatchSession.VictoryKillCount}";
            }
            if (resultTitle != null)
                resultTitle.text = session.Outcome == MatchOutcome.Victory ? "胜利" :
                    session.Outcome == MatchOutcome.Defeat ? "失败" : "结束";
        }

        private void OnState(GameState previous, GameState next)
        {
            if (content != null) content.SetActive(next == GameState.Playing || next == GameState.Paused);
        }

        private void OnDisable()
        {
            if (!bound) return;
            if (player != null) player.HealthChanged -= OnHealth;
            if (dash != null) dash.CooldownChanged -= OnCooldown;
            if (session != null) session.Changed -= OnSession;
            if (state != null) state.StateChanged -= OnState;
            bound = false;
        }
    }
}
