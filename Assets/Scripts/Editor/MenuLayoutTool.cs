using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// One-click tidy-up for the menus (Slice N Dice > Tidy Menu Layout). Every menu screen fills
// the whole screen like the title: the artwork covers the background (see MenuArtFit), the
// name runs across the top, arrows sit at the screen edges and Confirm at the bottom. The
// character and weapon screens give each player half the screen. Canvases scale against
// 1920x1080 and grow to fill wider or taller screens, so nothing is cut off and nothing
// shows through behind. Safe to run more than once; scene changes can be undone, then saved.
public static class MenuLayoutTool
{
    static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);
    const string FontPath = "Assets/TextMesh Pro/Examples & Extras/Resources/Fonts & Materials/Bangers SDF.asset";
    const string LevelPrefabPath = "Assets/Prefabs/Levels/FridgeLevel 1.prefab";
    const string ResultsPrefabPath = "Assets/Prefabs/UI/DeathPanel.prefab";

    // Shared layout for every menu screen.
    const float TitleBandHeight = 170f;         // Dark strip behind the name, so it reads over any art.
    const float TitleHeight = 130f;
    const float FullTitleSize = 110f;           // Main menu, options, stage.
    const float HalfTitleSize = 90f;            // Character, weapon (half screen each).
    const float ArrowFromEdge = 110f;
    const float ArrowWidth = 400f;              // The arrow art fills only the middle ~30% of its image.
    static readonly Vector2 ConfirmSize = new Vector2(380f, 96f);
    const float ConfirmFromBottom = 48f;
    const float ButtonTextSize = 46f;
    static readonly Color BandColor = new Color(0f, 0f, 0f, 0.45f);
    static readonly Color BackdropColor = new Color(0.05f, 0.05f, 0.08f, 1f);
    static readonly Color ButtonColor = new Color(0.72f, 0.06f, 0.16f, 1f);
    static readonly Color OutlineColor = new Color(0f, 0f, 0f, 0.75f);

    const string UndoName = "Tidy Menu Layout";

    static TMP_FontAsset font;
    static bool recordUndo = true;

    [MenuItem("Slice N Dice/Tidy Menu Layout")]
    static void TidyFromMenu()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!EditorUtility.DisplayDialog("Tidy Menu Layout",
                "Lay out every menu in '" + scene.name + "' and the Fridge level's results screen to fill the screen?\n\n" +
                "Scene changes can be undone (Ctrl+Z); save the scene afterwards to keep them. The level prefab is saved directly.",
                "Tidy", "Cancel"))
        {
            return;
        }

        Apply(scene, true);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    public static void Apply(Scene scene, bool includeLevelPrefab)
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

        Undo.SetCurrentGroupName(UndoName);
        int undoGroup = Undo.GetCurrentGroup();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (CanvasScaler scaler in root.GetComponentsInChildren<CanvasScaler>(true))
            {
                SetupScaler(scaler);
            }

            switch (root.name)
            {
                case "MainMenu": LayoutMainMenu(root.transform); break;
                case "SelectionMenus": LayoutSelectionMenus(root.transform); break;
                case "InGameUI": LayoutHud(root.transform); break;
            }
        }

        Undo.CollapseUndoOperations(undoGroup);

        if (includeLevelPrefab)
        {
            LayoutResults();
        }
    }

    /**************************/
    // Screens

    static void LayoutMainMenu(Transform root)
    {
        // A solid backdrop under every main-menu screen, so the 3D world never shows through.
        Backdrop(root, BackdropColor);

        Transform start = root.Find("StartScreenPanel");
        if (start != null)
        {
            LayoutStartScreen((RectTransform)start);
        }

        // These show the title artwork, so it always covers the screen like the title does.
        FullPanel(root.Find("MainMenuPanel"), Vector2.zero, Vector2.one, FullTitleSize, MenuArtFit.Mode.Cover);
        FullPanel(root.Find("OptionsPanel"), Vector2.zero, Vector2.one, FullTitleSize, MenuArtFit.Mode.Cover);

        Transform controls = root.Find("ControlsPanel");
        if (controls != null)
        {
            // The controls sheet is fitted to the screen (not cropped: it's all text and labels).
            Stretch((RectTransform)controls, 0f, 0f, 0f, 0f);
            MenuArtFit fit = ArtFit(controls.gameObject);
            fit.mode = MenuArtFit.Mode.Contain;
            fit.padLeft = fit.padRight = fit.padTop = fit.padBottom = 40f;
        }
    }

    static void LayoutStartScreen(RectTransform panel)
    {
        // Full-bleed title art that covers any screen shape without being squashed.
        Place(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, ReferenceResolution);
        Image art = panel.GetComponent<Image>();
        if (art != null && art.sprite != null)
        {
            AspectRatioFitter fitter = panel.GetComponent<AspectRatioFitter>();
            if (fitter == null) fitter = Add<AspectRatioFitter>(panel.gameObject);
            Record(fitter);
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = art.sprite.rect.width / art.sprite.rect.height;
        }

        foreach (TMP_Text text in panel.GetComponentsInChildren<TMP_Text>(true))
        {
            RectTransform rt = text.rectTransform;
            if (text.text.Contains("Slice"))
            {
                Place(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 290f), new Vector2(1600f, 240f));
                StyleText(text, 150f, 60f, TextAlignmentOptions.Center);
            }
            else
            {
                Place(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -360f), new Vector2(1200f, 100f));
                StyleText(text, 60f, 24f, TextAlignmentOptions.Center);
            }
        }
    }

    static void LayoutSelectionMenus(Transform root)
    {
        Transform background = root.Find("Background");
        if (background != null) Stretch((RectTransform)background, 0f, 0f, 0f, 0f);

        // Each player gets their own half of the screen. Art close to the panel's shape covers it;
        // tall art (weapons, the fridge) is shown whole on the panel's colour.
        FullPanel(root.Find("Player#1CharacterSelectPanel"), new Vector2(0f, 0f), new Vector2(0.5f, 1f), HalfTitleSize, MenuArtFit.Mode.Auto);
        FullPanel(root.Find("Player#2CharacterSelectPanel"), new Vector2(0.5f, 0f), new Vector2(1f, 1f), HalfTitleSize, MenuArtFit.Mode.Auto);
        FullPanel(root.Find("Player#1WeaponSelectPanel"), new Vector2(0f, 0f), new Vector2(0.5f, 1f), HalfTitleSize, MenuArtFit.Mode.Auto);
        FullPanel(root.Find("Player#2WeaponSelectPanel"), new Vector2(0.5f, 0f), new Vector2(1f, 1f), HalfTitleSize, MenuArtFit.Mode.Auto);

        FullPanel(root.Find("StageSelectPanel"), Vector2.zero, Vector2.one, FullTitleSize, MenuArtFit.Mode.Auto);
    }

    // Name, health (blood) and stamina bars in the top corners, mirrored for Player 2.
    static void LayoutHud(Transform root)
    {
        HudBlock(root.Find("Player1"), false);
        HudBlock(root.Find("Player2"), true);
    }

    static void HudBlock(Transform block, bool rightSide)
    {
        if (block == null) return;

        RectTransform rt = (RectTransform)block;
        Vector2 corner = rightSide ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
        Place(rt, corner, corner, corner, new Vector2(rightSide ? -70f : 70f, -50f), new Vector2(600f, 130f));

        int bar = 0;
        foreach (Transform child in block)
        {
            RectTransform childRect = (RectTransform)child;
            TMP_Text label = child.GetComponent<TMP_Text>();
            Slider slider = child.GetComponent<Slider>();
            if (label != null)
            {
                Place(childRect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 60f));
                StyleText(label, 48f, 24f, rightSide ? TextAlignmentOptions.Right : TextAlignmentOptions.Left);
            }
            else if (slider != null)
            {
                // First bar: health/blood (thicker), second: stamina.
                float y = bar == 0 ? -68f : -102f;
                float height = bar == 0 ? 26f : 18f;
                Place(childRect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(0f, height));
                Record(slider);
                slider.direction = rightSide ? Slider.Direction.RightToLeft : Slider.Direction.LeftToRight;
                bar++;
            }
        }
    }

    // The win/lose screen (StateChecker, "DeathPanel.prefab") is nested in the Fridge level prefab.
    // Each player's result art gets their half of the screen over a dark backdrop.
    static void LayoutResults()
    {
        // The backdrop goes in the results prefab itself, since a level can't reorder a nested
        // prefab's children. Check#1 is an empty marker that StateChecker switches on together
        // with the results, so a backdrop inside it darkens the arena only while they're up.
        EditPrefab(ResultsPrefabPath, checker =>
        {
            if (checker.Check1 == null) return;
            RectTransform marker = (RectTransform)checker.Check1.transform;
            marker.SetAsFirstSibling();
            Stretch(marker, 0f, 0f, 0f, 0f);
            Backdrop(marker, new Color(0f, 0f, 0f, 0.85f));
        });

        // The level already overrides the results layout, so it's set there.
        EditPrefab(LevelPrefabPath, checker =>
        {
            foreach (CanvasScaler scaler in checker.GetComponentsInChildren<CanvasScaler>(true))
            {
                SetupScaler(scaler);
            }

            ResultHalf(checker.Wpanel, false);
            ResultHalf(checker.Dpanel, true);
        });
    }

    static void EditPrefab(string path, System.Action<StateChecker> edit)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            return;
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        recordUndo = false;     // Prefabs are saved straight to disk; nothing to undo in the scene.
        try
        {
            StateChecker checker = contents.GetComponentInChildren<StateChecker>(true);
            if (checker != null)
            {
                edit(checker);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
            recordUndo = true;
        }
    }

    static void ResultHalf(GameObject card, bool rightSide)
    {
        if (card == null) return;

        // The art (assigned in game) fills its half, kept in proportion, with room for the caption.
        RectTransform rt = (RectTransform)card.transform;
        rt.localScale = Vector3.one;
        rt.anchorMin = rightSide ? new Vector2(0.5f, 0f) : Vector2.zero;
        rt.anchorMax = rightSide ? Vector2.one : new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(rightSide ? 30f : 60f, 110f);
        rt.offsetMax = new Vector2(rightSide ? -60f : -30f, -60f);
        Image art = card.GetComponent<Image>();
        if (art != null) art.preserveAspect = true;

        foreach (Text caption in card.GetComponentsInChildren<Text>(true))
        {
            RectTransform captionRect = caption.rectTransform;
            captionRect.localScale = Vector3.one;
            captionRect.anchorMin = new Vector2(0f, 0f);
            captionRect.anchorMax = new Vector2(1f, 0f);
            captionRect.pivot = new Vector2(0.5f, 1f);
            captionRect.anchoredPosition = new Vector2(0f, -14f);
            captionRect.sizeDelta = new Vector2(0f, 64f);
            caption.fontSize = 40;
            caption.alignment = TextAnchor.MiddleCenter;
            caption.horizontalOverflow = HorizontalWrapMode.Wrap;
            caption.verticalOverflow = VerticalWrapMode.Truncate;
            caption.resizeTextForBestFit = true;
            caption.resizeTextMinSize = 18;
            caption.resizeTextMaxSize = 40;
            caption.color = Color.white;
            Outline outline = caption.GetComponent<Outline>();
            if (outline == null) outline = caption.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
            outline.effectDistance = new Vector2(2f, -2f);
        }
    }

    /**************************/
    // The shared full-screen menu panel

    // A carousel screen filling the given part of the screen: its colour and artwork behind
    // everything, the name across the top on a dark strip, arrows at the edges, Confirm at the
    // bottom. Children are recognised by their names, as in every menu here.
    static void FullPanel(Transform panelTransform, Vector2 anchorMin, Vector2 anchorMax, float titleSize, MenuArtFit.Mode artMode)
    {
        if (panelTransform == null) return;

        RectTransform panel = (RectTransform)panelTransform;
        Record(panel);
        panel.localScale = Vector3.one;
        panel.localRotation = Quaternion.identity;
        panel.anchorMin = anchorMin;
        panel.anchorMax = anchorMax;
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.offsetMin = Vector2.zero;
        panel.offsetMax = Vector2.zero;

        // Covering art can be bigger than the panel; keep it inside (matters for the half screens).
        if (panel.GetComponent<RectMask2D>() == null)
        {
            Add<RectMask2D>(panel.gameObject);
        }

        TMP_Text title = null;
        foreach (Transform child in panel)
        {
            RectTransform rt = (RectTransform)child;
            string name = child.name;

            if (name == "BackgroundColor" || name == "Mask")
            {
                Stretch(rt, 0f, 0f, 0f, 0f);
            }
            else if (name.EndsWith("Splash"))
            {
                Stretch(rt, 0f, 0f, 0f, 0f);
                MenuArtFit fit = ArtFit(child.gameObject);
                fit.mode = artMode;
                fit.padLeft = fit.padRight = ArrowFromEdge * 2f;
                fit.padTop = TitleBandHeight + 10f;
                fit.padBottom = ConfirmFromBottom + ConfirmSize.y + 24f;
            }
            else if (name.StartsWith("Left"))
            {
                Place(rt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(ArrowFromEdge, 0f), ArrowSizeFor(child));
                ArrowStyle(child.GetComponent<Image>());
            }
            else if (name.StartsWith("Right"))
            {
                Place(rt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-ArrowFromEdge, 0f), ArrowSizeFor(child));
                ArrowStyle(child.GetComponent<Image>());
            }
            else if (name == "ConfirmButton")
            {
                Place(rt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, ConfirmFromBottom), ConfirmSize);
                ButtonStyle(child.GetComponent<Button>());
                foreach (TMP_Text label in child.GetComponentsInChildren<TMP_Text>(true))
                {
                    Stretch(label.rectTransform, 8f, 8f, 4f, 4f);
                    StyleText(label, ButtonTextSize, 20f, TextAlignmentOptions.Center);
                    Record(label);
                    label.text = "CONFIRM";
                    label.color = Color.white;
                }
            }
            else if (child.GetComponent<TMP_Text>() != null)
            {
                title = child.GetComponent<TMP_Text>();
                Place(rt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(-160f, TitleHeight));
                StyleText(title, titleSize, 36f, TextAlignmentOptions.Center);
            }
        }

        if (title != null)
        {
            TitleBand(panel, title.transform);
        }
    }

    // A dark strip across the top of a panel, drawn just behind its title.
    static void TitleBand(RectTransform panel, Transform title)
    {
        Transform band = panel.Find("TitleBand");
        if (band == null)
        {
            GameObject go = new GameObject("TitleBand", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Created(go);
            go.layer = panel.gameObject.layer;
            go.transform.SetParent(panel, false);
            band = go.transform;
        }

        Record(band);
        band.SetSiblingIndex(Mathf.Max(0, title.GetSiblingIndex() - (band.GetSiblingIndex() < title.GetSiblingIndex() ? 1 : 0)));
        Place((RectTransform)band, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, TitleBandHeight));
        Image image = band.GetComponent<Image>();
        Record(image);
        image.color = BandColor;
        image.raycastTarget = false;
    }

    // A full-screen solid image behind everything else under the given canvas.
    static void Backdrop(Transform canvas, Color color)
    {
        Transform backdrop = canvas.Find("Backdrop");
        if (backdrop == null)
        {
            GameObject go = new GameObject("Backdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Created(go);
            go.layer = canvas.gameObject.layer;
            go.transform.SetParent(canvas, false);
            backdrop = go.transform;
        }

        Record(backdrop);
        backdrop.SetAsFirstSibling();
        Stretch((RectTransform)backdrop, 0f, 0f, 0f, 0f);
        Image image = backdrop.GetComponent<Image>();
        Record(image);
        image.color = color;
        image.raycastTarget = false;
    }

    /**************************/
    // Helpers

    static void Record(Object target)
    {
        if (recordUndo) Undo.RecordObject(target, UndoName);
    }

    static void Created(GameObject go)
    {
        if (recordUndo) Undo.RegisterCreatedObjectUndo(go, UndoName);
    }

    static T Add<T>(GameObject go) where T : Component
    {
        return recordUndo ? Undo.AddComponent<T>(go) : go.AddComponent<T>();
    }

    static MenuArtFit ArtFit(GameObject go)
    {
        MenuArtFit fit = go.GetComponent<MenuArtFit>();
        if (fit == null) fit = Add<MenuArtFit>(go);
        Record(fit);
        return fit;
    }

    // Arrow buttons keep the shape of their artwork. The arrow images have a lot of empty
    // space around the arrow, so the button is larger than the arrow you see.
    static Vector2 ArrowSizeFor(Transform arrow)
    {
        Image image = arrow.GetComponent<Image>();
        float aspect = image != null && image.sprite != null ? image.sprite.rect.width / image.sprite.rect.height : 1f;
        return new Vector2(ArrowWidth, ArrowWidth / aspect);
    }

    static void SetupScaler(CanvasScaler scaler)
    {
        Record(scaler);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
    }

    static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
    {
        Record(rt);
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
    }

    // Fill the parent, inset by the given margins.
    static void Stretch(RectTransform rt, float left, float right, float top, float bottom)
    {
        Record(rt);
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    // Arrows keep their shape and get a dark edge so they read over bright artwork.
    static void ArrowStyle(Image image)
    {
        if (image == null) return;
        Record(image);
        image.preserveAspect = true;
        Outline outline = image.GetComponent<Outline>();
        if (outline == null) outline = Add<Outline>(image.gameObject);
        Record(outline);
        outline.effectColor = OutlineColor;
        outline.effectDistance = new Vector2(4f, -4f);
    }

    // The same solid button on every screen (they were see-through red before).
    static void ButtonStyle(Button button)
    {
        if (button == null) return;
        Image image = button.targetGraphic as Image;
        if (image != null)
        {
            Record(image);
            image.color = ButtonColor;
        }

        Record(button);
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.85f, 0.85f, 1f);
        colors.selectedColor = Color.white;
        colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.6f);
        button.colors = colors;
    }

    static void StyleText(TMP_Text text, float size, float minSize, TextAlignmentOptions alignment)
    {
        Record(text);
        if (font != null)
        {
            text.font = font;
            text.fontSharedMaterial = font.material;
        }

        text.enableAutoSizing = true;
        text.fontSizeMax = size;
        text.fontSizeMin = minSize;
        text.fontSize = size;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.characterSpacing = 2f;
    }
}
