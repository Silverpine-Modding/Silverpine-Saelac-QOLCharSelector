#nullable enable

using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Silverpine.ModdingTools;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SilverpineMods.QOLCharSelector;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(ModdingToolsGuid, ModdingToolsVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid =
        "renegadex.silverpine.qolcharselector";
    public const string PluginName = "QOLCharSelector";
    public const string PluginVersion = "1.0.1";
    public const string ModdingToolsGuid =
        "Saelac.Silverpine.ModdingTools";
    public const string ModdingToolsVersion = "1.9.3";

    internal static ManualLogSource Log { get; private set; } = null!;

    private void Awake()
    {
        Log = Logger;
        Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, PluginGuid);
        Log.LogInfo(
            $"{PluginName} {PluginVersion} installed its process-lifetime "
            + "main-menu hook.");
    }

    private void OnDestroy()
    {
        // Silverpine destroys this bootstrap host before gameplay. Following
        // ModdingTools' lifecycle contract, the static Harmony hook remains
        // installed until the process exits.
        Logger.LogInfo(
            "Plugin host destroyed during Silverpine bootstrap; the "
            + "character-list hook remains installed for the process "
            + "lifetime.");
    }
}

[HarmonyPatch(typeof(MainMenuUI), "Awake")]
internal static class MainMenuCharacterListPatch
{
    private static void Postfix(MainMenuUI __instance)
    {
        try
        {
            CharacterSelectionListController? controller =
                __instance.GetComponentInChildren<
                    CharacterSelectionListController>(includeInactive: true);
            if (controller == null)
            {
                Transform characterCreation =
                    __instance.characterSelectionUI.transform.parent;
                controller = characterCreation.gameObject.AddComponent<
                    CharacterSelectionListController>();
            }

            controller.Initialize(__instance);
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError(
                "Could not add the start-screen character list:\n"
                + exception);
        }
    }
}

internal sealed class CharacterSelectionListController : MonoBehaviour
{
    private const float PanelWidth = 284f;
    private const float PanelGap = 20f;
    private const float HeaderHeight = 48f;
    private const float SearchHeight = 42f;
    private const float SearchGap = 6f;
    private const float RowHeight = 62f;
    private const float RowSpacing = 5f;
    private const float ScrollbarWidth = 14f;

    private static readonly Color PanelColor =
        new(0.055f, 0.045f, 0.035f, 0.96f);
    private static readonly Color ViewportColor =
        new(0f, 0f, 0f, 0.16f);
    private static readonly Color SelectedColor =
        new(1f, 0.79f, 0.38f, 1f);
    private static readonly Color UnselectedBarColor =
        new(1f, 1f, 1f, 0f);

    private sealed class CharacterRow
    {
        internal CharacterField Character = null!;
        internal Button Button = null!;
        internal Image SelectionBar = null!;
        internal TextMeshProUGUI Label = null!;
        internal Color NormalTextColor;
    }

    private readonly List<CharacterRow> rows = new();

    private MainMenuUI mainMenu = null!;
    private SwitchSelectionUI selector = null!;
    private Button buttonTemplate = null!;
    private RectTransform panel = null!;
    private RectTransform viewport = null!;
    private RectTransform content = null!;
    private ScrollRect scrollRect = null!;
    private TextMeshProUGUI title = null!;
    private TMP_InputField searchInput = null!;
    private string searchText = "";
    private int knownChildCount = -1;
    private GameObject? knownSelection;
    private bool initialized;
    private bool scrollToSelection;

    internal void Initialize(MainMenuUI menu)
    {
        if (initialized)
            return;

        mainMenu = menu ?? throw new ArgumentNullException(nameof(menu));
        selector = mainMenu.characterSelectionUI ??
            throw new InvalidOperationException(
                "MainMenuUI has no character selector.");
        buttonTemplate = FindButtonTemplate();

        CreatePanel();
        selector.OnAnySelected += OnSelectionChanged;
        initialized = true;
        RebuildRows();

        Plugin.Log.LogInfo(
            "Added a scrollable character list beside the start-screen "
            + "character selector.");
    }

    private Button FindButtonTemplate()
    {
        Button? template = mainMenu
            .GetComponentsInChildren<Button>(includeInactive: true)
            .FirstOrDefault(button => button.name == "CreateCustomButton")
            ?? mainMenu
                .GetComponentsInChildren<Button>(includeInactive: true)
                .FirstOrDefault(button =>
                    button.GetComponentInChildren<TextMeshProUGUI>(true)
                    != null);

        return template ?? throw new InvalidOperationException(
            "Could not find a native start-screen button template.");
    }

    private void CreatePanel()
    {
        RectTransform characterCreation =
            (RectTransform)selector.transform.parent;
        RectTransform selectorRect = (RectTransform)selector.transform;

        GameObject panelObject = new(
            "Character Selection List",
            typeof(RectTransform),
            typeof(Image));
        panel = panelObject.GetComponent<RectTransform>();
        panel.SetParent(characterCreation, worldPositionStays: false);
        panel.SetSiblingIndex(0);
        panel.anchorMin = new Vector2(0.5f, 0.5f);
        panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(
            PanelWidth,
            Mathf.Clamp(selectorRect.rect.height, 360f, 520f));
        panel.anchoredPosition = new Vector2(
            -characterCreation.rect.width * 0.5f
                - PanelWidth * 0.5f
                - PanelGap,
            selectorRect.anchoredPosition.y);

        Image panelBackground = panelObject.GetComponent<Image>();
        Image? nativeBackground = selector.transform
            .GetChild(0)
            .Find("Background")
            ?.GetComponent<Image>();
        if (nativeBackground != null && nativeBackground.sprite != null)
        {
            panelBackground.sprite = nativeBackground.sprite;
            panelBackground.type = nativeBackground.type;
        }
        panelBackground.color = PanelColor;
        panelBackground.raycastTarget = true;

        CreateTitle();
        CreateSearchField();
        CreateScrollArea();
    }

    private void CreateTitle()
    {
        title = ModUi.CloneTitle(
            buttonTemplate,
            panel,
            "Characters",
            HeaderHeight);
        title.name = "Character List Title";
        title.text = "Characters";
        title.alignment = TextAlignmentOptions.Center;
        title.enableWordWrapping = false;
        title.overflowMode = TextOverflowModes.Ellipsis;
        title.enableAutoSizing = true;
        title.fontSizeMin = 19f;
        title.fontSizeMax = 30f;
        title.raycastTarget = false;

        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = Vector2.one;
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = Vector2.zero;
        titleRect.sizeDelta = new Vector2(0f, HeaderHeight);
    }

    private void CreateSearchField()
    {
        GameObject inputObject = new(
            "Character Search",
            typeof(RectTransform),
            typeof(Image),
            typeof(TMP_InputField));
        RectTransform inputRect =
            inputObject.GetComponent<RectTransform>();
        inputRect.SetParent(panel, worldPositionStays: false);
        inputRect.anchorMin = new Vector2(0f, 1f);
        inputRect.anchorMax = Vector2.one;
        inputRect.pivot = new Vector2(0.5f, 1f);
        inputRect.anchoredPosition = new Vector2(0f, -HeaderHeight);
        inputRect.sizeDelta = new Vector2(-16f, SearchHeight);

        TMP_InputField? nativeInput = mainMenu
            .GetComponentsInChildren<TMP_InputField>(includeInactive: true)
            .FirstOrDefault();
        Image background = inputObject.GetComponent<Image>();
        if (nativeInput?.targetGraphic is Image nativeImage)
        {
            background.sprite = nativeImage.sprite;
            background.type = nativeImage.type;
            background.color = nativeImage.color;
        }
        else if (buttonTemplate.targetGraphic is Image buttonImage)
        {
            background.sprite = buttonImage.sprite;
            background.type = buttonImage.type;
            background.color = buttonImage.color;
        }
        else
        {
            background.color = new Color(0.18f, 0.14f, 0.10f, 1f);
        }

        GameObject textAreaObject = new(
            "Text Area",
            typeof(RectTransform),
            typeof(RectMask2D));
        RectTransform textArea =
            textAreaObject.GetComponent<RectTransform>();
        textArea.SetParent(inputRect, worldPositionStays: false);
        textArea.anchorMin = Vector2.zero;
        textArea.anchorMax = Vector2.one;
        textArea.offsetMin = new Vector2(12f, 5f);
        textArea.offsetMax = new Vector2(-12f, -5f);

        TextMeshProUGUI textSource =
            nativeInput?.textComponent as TextMeshProUGUI
            ?? buttonTemplate.GetComponentInChildren<TextMeshProUGUI>(true);
        TextMeshProUGUI inputText = Instantiate(
            textSource,
            textArea,
            worldPositionStays: false);
        ConfigureInputText(inputText, "");
        inputText.name = "Text";

        TextMeshProUGUI placeholder = Instantiate(
            textSource,
            textArea,
            worldPositionStays: false);
        ConfigureInputText(placeholder, "Search characters...");
        placeholder.name = "Placeholder";
        placeholder.fontStyle = FontStyles.Italic;
        Color placeholderColor = placeholder.color;
        placeholderColor.a = 0.55f;
        placeholder.color = placeholderColor;

        searchInput = inputObject.GetComponent<TMP_InputField>();
        searchInput.targetGraphic = background;
        searchInput.textViewport = textArea;
        searchInput.textComponent = inputText;
        searchInput.placeholder = placeholder;
        searchInput.lineType = TMP_InputField.LineType.SingleLine;
        searchInput.contentType = TMP_InputField.ContentType.Standard;
        searchInput.characterLimit = 80;
        searchInput.onFocusSelectAll = false;
        searchInput.colors = nativeInput?.colors ?? buttonTemplate.colors;
        searchInput.SetTextWithoutNotify(searchText);
        searchInput.onValueChanged.AddListener(OnSearchChanged);
    }

    private static void ConfigureInputText(
        TextMeshProUGUI text,
        string value)
    {
        text.text = value;
        text.richText = false;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.enableAutoSizing = true;
        text.fontSizeMin = 14f;
        text.fontSizeMax = 23f;
        text.raycastTarget = false;

        RectTransform rect = text.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
    }

    private void CreateScrollArea()
    {
        GameObject scrollObject = new(
            "Character List Scroll View",
            typeof(RectTransform),
            typeof(ScrollRect));
        RectTransform scrollHost =
            scrollObject.GetComponent<RectTransform>();
        scrollHost.SetParent(panel, worldPositionStays: false);
        scrollHost.anchorMin = Vector2.zero;
        scrollHost.anchorMax = Vector2.one;
        scrollHost.offsetMin = new Vector2(8f, 8f);
        scrollHost.offsetMax = new Vector2(
            -8f,
            -(HeaderHeight + SearchHeight + SearchGap));

        GameObject viewportObject = new(
            "Viewport",
            typeof(RectTransform),
            typeof(Image),
            typeof(RectMask2D));
        viewport = viewportObject.GetComponent<RectTransform>();
        viewport.SetParent(scrollHost, worldPositionStays: false);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = new Vector2(
            -(ScrollbarWidth + 6f),
            0f);
        Image viewportImage = viewportObject.GetComponent<Image>();
        viewportImage.color = ViewportColor;
        viewportImage.raycastTarget = true;

        GameObject contentObject = new(
            "Characters",
            typeof(RectTransform),
            typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter));
        content = contentObject.GetComponent<RectTransform>();
        content.SetParent(viewport, worldPositionStays: false);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;

        VerticalLayoutGroup layout =
            contentObject.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(4, 4, 4, 4);
        layout.spacing = RowSpacing;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter =
            contentObject.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Scrollbar scrollbar = CreateScrollbar(scrollHost);
        scrollRect = scrollObject.GetComponent<ScrollRect>();
        scrollRect.viewport = viewport;
        scrollRect.content = content;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.inertia = true;
        scrollRect.decelerationRate = 0.135f;
        scrollRect.scrollSensitivity = 42f;
        scrollRect.verticalScrollbar = scrollbar;
        scrollRect.verticalScrollbarVisibility =
            ScrollRect.ScrollbarVisibility.AutoHide;
        scrollRect.verticalScrollbarSpacing = 6f;
    }

    private static Scrollbar CreateScrollbar(RectTransform parent)
    {
        GameObject scrollbarObject = new(
            "Scrollbar",
            typeof(RectTransform),
            typeof(Image),
            typeof(Scrollbar));
        RectTransform scrollbarRect =
            scrollbarObject.GetComponent<RectTransform>();
        scrollbarRect.SetParent(parent, worldPositionStays: false);
        scrollbarRect.anchorMin = new Vector2(1f, 0f);
        scrollbarRect.anchorMax = Vector2.one;
        scrollbarRect.pivot = new Vector2(1f, 0.5f);
        scrollbarRect.offsetMin = new Vector2(-ScrollbarWidth, 0f);
        scrollbarRect.offsetMax = Vector2.zero;

        Image background = scrollbarObject.GetComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.34f);

        GameObject slidingObject = new(
            "Sliding Area",
            typeof(RectTransform));
        RectTransform sliding =
            slidingObject.GetComponent<RectTransform>();
        sliding.SetParent(scrollbarRect, worldPositionStays: false);
        sliding.anchorMin = Vector2.zero;
        sliding.anchorMax = Vector2.one;
        sliding.offsetMin = new Vector2(3f, 3f);
        sliding.offsetMax = new Vector2(-3f, -3f);

        GameObject handleObject = new(
            "Handle",
            typeof(RectTransform),
            typeof(Image));
        RectTransform handle = handleObject.GetComponent<RectTransform>();
        handle.SetParent(sliding, worldPositionStays: false);
        handle.anchorMin = Vector2.zero;
        handle.anchorMax = Vector2.one;
        handle.offsetMin = Vector2.zero;
        handle.offsetMax = Vector2.zero;
        Image handleImage = handleObject.GetComponent<Image>();
        handleImage.color = new Color(0.78f, 0.68f, 0.50f, 0.95f);

        Scrollbar scrollbar = scrollbarObject.GetComponent<Scrollbar>();
        scrollbar.handleRect = handle;
        scrollbar.targetGraphic = handleImage;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.size = 0.25f;
        return scrollbar;
    }

    private void LateUpdate()
    {
        if (!initialized || selector == null)
            return;

        if (knownChildCount != selector.transform.childCount)
            RebuildRows();

        GameObject? selection = selector.GetSelected();
        if (selection != knownSelection)
            OnSelectionChanged();

        if (!scrollToSelection)
            return;

        scrollToSelection = false;
        EnsureSelectedRowVisible();
    }

    private void RebuildRows()
    {
        if (content == null)
            return;

        foreach (Transform child in content.Cast<Transform>().ToArray())
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
        rows.Clear();

        for (int index = 0; index < selector.transform.childCount; index++)
        {
            CharacterField? character = selector.transform
                .GetChild(index)
                .GetComponent<CharacterField>();
            if (character != null)
                CreateRow(character, index);
        }

        knownChildCount = selector.transform.childCount;
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        ApplySearchFilter(resetScroll: false);
        OnSelectionChanged();
    }

    private void CreateRow(CharacterField character, int selectorIndex)
    {
        string name = string.IsNullOrWhiteSpace(character.defaultCharacterName)
            ? character.gameObject.name.Replace("Field", "")
            : character.defaultCharacterName.Trim();
        int capturedIndex = selectorIndex;
        Button rowButton = ModUi.CloneButton(
            buttonTemplate,
            content,
            name,
            () => SelectCharacter(capturedIndex),
            RowHeight);
        rowButton.name = "Character " + name;

        ButtonSoundPlayer? sound =
            rowButton.GetComponent<ButtonSoundPlayer>();
        sound?.Subscribe();

        HorizontalLayoutGroup rowLayout =
            rowButton.gameObject.AddComponent<HorizontalLayoutGroup>();
        rowLayout.padding = new RectOffset(7, 9, 6, 6);
        rowLayout.spacing = 9f;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;

        GameObject barObject = new(
            "Selected",
            typeof(RectTransform),
            typeof(Image),
            typeof(LayoutElement));
        barObject.transform.SetParent(
            rowButton.transform,
            worldPositionStays: false);
        barObject.transform.SetSiblingIndex(0);
        Image selectionBar = barObject.GetComponent<Image>();
        selectionBar.color = UnselectedBarColor;
        selectionBar.raycastTarget = false;
        LayoutElement barLayout = barObject.GetComponent<LayoutElement>();
        barLayout.minWidth = 4f;
        barLayout.preferredWidth = 4f;
        barLayout.preferredHeight = 46f;

        GameObject iconObject = new(
            "Character Sprite",
            typeof(RectTransform),
            typeof(Image),
            typeof(LayoutElement));
        iconObject.transform.SetParent(
            rowButton.transform,
            worldPositionStays: false);
        iconObject.transform.SetSiblingIndex(1);
        Image icon = iconObject.GetComponent<Image>();
        icon.sprite = ResolveSmallSprite(character);
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        LayoutElement iconLayout = iconObject.GetComponent<LayoutElement>();
        iconLayout.minWidth = 46f;
        iconLayout.preferredWidth = 46f;
        iconLayout.minHeight = 46f;
        iconLayout.preferredHeight = 46f;

        TextMeshProUGUI label = rowButton
            .GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
        label.text = name;
        label.richText = false;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.enableAutoSizing = true;
        label.fontSizeMin = 15f;
        label.fontSizeMax = 25f;
        label.raycastTarget = false;
        LayoutElement labelLayout =
            label.GetComponent<LayoutElement>() ??
            label.gameObject.AddComponent<LayoutElement>();
        labelLayout.minWidth = 0f;
        labelLayout.flexibleWidth = 1f;
        labelLayout.preferredHeight = 46f;

        rows.Add(new CharacterRow
        {
            Character = character,
            Button = rowButton,
            SelectionBar = selectionBar,
            Label = label,
            NormalTextColor = label.color
        });
    }

    private static Sprite? ResolveSmallSprite(CharacterField character)
    {
        string name = character.defaultCharacterName;
        if (!string.IsNullOrWhiteSpace(name)
            && CustomContentDefinition_PlayerCharacter.loaded.TryGetValue(
                name,
                out CustomContentDefinition_PlayerCharacter definition)
            && definition.assets.smallSprite != null)
        {
            return definition.assets.smallSprite;
        }

        Sprite? sprite = string.IsNullOrWhiteSpace(name)
            ? null
            : Resources.Load<Sprite>(
                "Sprites/Player/sprite_player_"
                + name.ToLowerInvariant()
                + "_small");
        return sprite ?? character.transform
            .Find("CharacterSprite")
            ?.GetComponent<Image>()
            ?.sprite;
    }

    private void SelectCharacter(int index)
    {
        if (index < 0 || index >= selector.transform.childCount)
            return;
        selector.SetIndex(index);
    }

    private void OnSearchChanged(string value)
    {
        searchText = value ?? "";
        ApplySearchFilter(resetScroll: true);
    }

    private void ApplySearchFilter(bool resetScroll)
    {
        string filter = searchText.Trim();
        int visibleCount = 0;
        foreach (CharacterRow row in rows)
        {
            bool visible = filter.Length == 0
                || row.Label.text.IndexOf(
                    filter,
                    StringComparison.OrdinalIgnoreCase) >= 0;
            row.Button.gameObject.SetActive(visible);
            if (visible)
                visibleCount++;
        }

        title.text = filter.Length == 0
            ? $"Characters ({rows.Count})"
            : $"Characters ({visibleCount}/{rows.Count})";
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        if (resetScroll)
        {
            content.anchoredPosition = Vector2.zero;
            scrollRect.StopMovement();
            scrollRect.verticalNormalizedPosition = 1f;
        }
    }

    private void OnSelectionChanged()
    {
        if (!initialized && rows.Count == 0)
            return;

        knownSelection = selector.GetSelected();
        foreach (CharacterRow row in rows)
        {
            // A custom Player Character can be removed synchronously while
            // SwitchSelectionUI is notifying its listeners. Unity keeps the
            // managed CharacterField wrapper until this list rebuilds on the
            // next LateUpdate, but accessing gameObject on that destroyed
            // wrapper throws a NullReferenceException.
            bool selected = row.Character != null
                && knownSelection == row.Character.gameObject;
            row.SelectionBar.color = selected
                ? SelectedColor
                : UnselectedBarColor;
            row.Label.color = selected
                ? SelectedColor
                : row.NormalTextColor;
        }
        scrollToSelection = knownSelection != null;
    }

    private void EnsureSelectedRowVisible()
    {
        CharacterRow? selected = rows.FirstOrDefault(
            row => row.Character != null
                && row.Character.gameObject == knownSelection);
        if (selected == null
            || !selected.Button.gameObject.activeSelf
            || viewport == null
            || content == null)
            return;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        Bounds rowBounds = RectTransformUtility
            .CalculateRelativeRectTransformBounds(
                viewport,
                selected.Button.transform);
        Vector2 position = content.anchoredPosition;
        if (rowBounds.max.y > viewport.rect.yMax)
        {
            position.y -= rowBounds.max.y - viewport.rect.yMax;
        }
        else if (rowBounds.min.y < viewport.rect.yMin)
        {
            position.y += viewport.rect.yMin - rowBounds.min.y;
        }

        float maximum = Mathf.Max(
            0f,
            content.rect.height - viewport.rect.height);
        position.x = 0f;
        position.y = Mathf.Clamp(position.y, 0f, maximum);
        content.anchoredPosition = position;
        scrollRect.StopMovement();
    }

    private void OnDestroy()
    {
        // This component is scene-local, unlike the BepInEx bootstrap host.
        if (initialized && selector != null)
            selector.OnAnySelected -= OnSelectionChanged;
    }
}
