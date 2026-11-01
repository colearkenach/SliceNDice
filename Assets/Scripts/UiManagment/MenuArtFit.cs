using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Sizes a menu's artwork inside its parent panel without squashing it. When the art is close to
// the panel's shape it covers the whole panel (like the title screen, cropping a little); when
// it's very different (a tall knife on a wide screen) it fits inside instead and the panel's
// background colour fills the rest. Follows the sprite swaps the menu scripts make.
[ExecuteAlways]
[RequireComponent(typeof(Image))]
public class MenuArtFit : MonoBehaviour
{
    public enum Mode { Auto, Cover, Contain }

    public Mode mode = Mode.Auto;
    [Tooltip("Auto: cover when the art's proportions are within this factor of the panel's, otherwise contain.")]
    public float coverTolerance = 1.6f;
    [Tooltip("Contain only: space kept clear around the art (for the title, arrows and Confirm).")]
    public float padLeft = 200f;
    public float padRight = 200f;
    public float padTop = 170f;
    public float padBottom = 170f;

    static readonly List<MenuArtFit> active = new List<MenuArtFit>();

    Image image;
    Sprite lastSprite;
    Vector2 lastArea;

    void OnEnable()
    {
        active.Add(this);
        Refit(true);
    }

    void OnDisable()
    {
        active.Remove(this);
    }

    void LateUpdate()
    {
        Refit(false);
    }

    public static void RefitAll()
    {
        foreach (MenuArtFit fit in active)
        {
            fit.Refit(true);
        }
    }

    public void Refit(bool force)
    {
        if (image == null) image = GetComponent<Image>();
        RectTransform parent = transform.parent as RectTransform;
        if (image == null || image.sprite == null || parent == null)
        {
            return;
        }

        Vector2 area = parent.rect.size;
        if (!force && image.sprite == lastSprite && area == lastArea)
        {
            return;
        }

        lastSprite = image.sprite;
        lastArea = area;
        if (area.x <= 0f || area.y <= 0f)
        {
            return;
        }

        float art = image.sprite.rect.width / image.sprite.rect.height;
        float box = area.x / area.y;
        bool cover = mode == Mode.Cover || (mode == Mode.Auto && Mathf.Max(art / box, box / art) <= coverTolerance);

        RectTransform rt = (RectTransform)transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        image.preserveAspect = !cover;

        if (cover)
        {
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = art > box ? new Vector2(area.y * art, area.y) : new Vector2(area.x, area.x / art);
        }
        else
        {
            Vector2 inner = new Vector2(Mathf.Max(1f, area.x - padLeft - padRight), Mathf.Max(1f, area.y - padTop - padBottom));
            Vector2 size = inner.x / inner.y > art ? new Vector2(inner.y * art, inner.y) : new Vector2(inner.x, inner.x / art);
            rt.anchoredPosition = new Vector2((padLeft - padRight) * 0.5f, (padBottom - padTop) * 0.5f);
            rt.sizeDelta = size;
        }
    }
}
