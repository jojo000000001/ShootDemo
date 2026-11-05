using UnityEngine;

namespace ShootDemo.Player
{
    /// <summary>
    /// Plants the authored neo_yinying sprite on the ground under the player.
    /// It is parented to the rotating visual root and sorted beneath the floor art.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerFootShadow : MonoBehaviour
    {
        [SerializeField] private string shadowName = "neo_yinying";
        [SerializeField] private Vector2 footOffset = new Vector2(0f, -0.18f);
        [SerializeField] private Vector2 shadowScale = new Vector2(0.7f, 0.34f);
        [SerializeField] private int sortingOrder = 8;
        [SerializeField] private float alpha = 0.5f;

        private Transform shadow;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AttachToPlayer()
        {
            GameObject player = GameObject.Find("neo");
            if (player == null || player.GetComponent<PlayerFootShadow>() != null)
            {
                return;
            }

            player.AddComponent<PlayerFootShadow>();
        }

        private void Awake()
        {
            shadow = FindNamed(transform, shadowName);
            if (shadow == null)
            {
                enabled = false;
                return;
            }

            shadow.SetParent(transform, false);
            ApplyPose();
        }

        private void LateUpdate()
        {
            if (shadow == null)
            {
                return;
            }

            ApplyPose();
        }

        private void ApplyPose()
        {
            shadow.localPosition = new Vector3(footOffset.x, footOffset.y, 0f);
            shadow.localRotation = Quaternion.identity;
            shadow.localScale = new Vector3(shadowScale.x, shadowScale.y, 1f);

            SpriteRenderer renderer = shadow.GetComponent<SpriteRenderer>();
            if (renderer == null)
            {
                return;
            }

            renderer.sortingOrder = sortingOrder;
            Color color = renderer.color;
            color.a = alpha;
            renderer.color = color;
        }

        private static Transform FindNamed(Transform root, string childName)
        {
            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].name == childName)
                {
                    return children[i];
                }
            }

            return null;
        }
    }
}
