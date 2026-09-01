using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WordSortPuzzle
{
    public sealed class WordSortGame : MonoBehaviour
    {
        [Header("Prepared scene art")] [SerializeField]
        private Sprite[] letterSprites;

        [Header("Development testing")]
        [Tooltip("Lets PLAY open the test level and temporarily unlocks every level in the level selector.")]
        [SerializeField]
        private bool enableLevelTesting;

        [Tooltip("One-based level number. Level 151 is the first level with 6-letter words.")]
        [SerializeField, Range(1, WordLevelCatalog.TotalLevels)]
        private int testLevelNumber = 151;

        private readonly List<Tube> tubes = new List<Tube>();
        private readonly List<TargetChip> targetChips = new List<TargetChip>();
        private readonly List<LevelButtonView> levelButtons = new List<LevelButtonView>();
        private readonly List<TextMeshProUGUI> coinTexts = new List<TextMeshProUGUI>();
        private readonly List<RectTransform> loadingTiles = new List<RectTransform>();
        private readonly Stack<MoveRecord> undoStack = new Stack<MoveRecord>();
        private readonly HashSet<int> hiddenWordIndices = new HashSet<int>();
        private readonly List<BackgroundCardView> backgroundCards = new List<BackgroundCardView>();

        private RectTransform gameRoot, mainScreen, levelScreen, playScreen, backgroundScreen;
        private RectTransform targetContainer, boardHost, boardContent, piecesLayer;
        private Image sceneBackground, backgroundShade;
        private CanvasGroup settingsPanel, shopPanel, winPanel, loadingPanel, privacyPanel;
        private TextMeshProUGUI mainProgressText, levelTitleText, helperText, moveText;
        private TextMeshProUGUI winTitleText, winStatsText, nextButtonText;
        private TextMeshProUGUI soundValueText, musicValueText, vibrationValueText;
        private TextMeshProUGUI undoButtonText, shopUndoText, shopStatusText;
        private TextMeshProUGUI smallPackPriceText, mediumPackPriceText, largePackPriceText, removeAdsPriceText;
        private TextMeshProUGUI loadingStatusText;
        private Image loadingProgressFill;
        private RectTransform loadingSpinner;
        private AudioSource uiAudio, musicAudio;
        private AudioClip clickClip, successClip;
        private Sprite[] backgroundSprites;
        private Sprite roundedButtonSprite;
        private Texture2D roundedButtonTexture;
        private Sprite settingsIcon, backIcon, undoIcon, resetIcon, gameLogo;
        private MonetizationManager monetization;

        private WordLevelDefinition level;
        private List<List<char>> initialStacks;
        private Tube selectedTube;
        private Tube bonusTube;
        private int currentLevelIndex, moves, completionReward, freeUndosRemaining;
        private bool isAnimating, hasWon, isLoading;
        private Coroutine startupRoutine;

        private const int BonusTubeCost = 50;
        private const int FirstCompletionReward = 15;
        private const int FreeUndosPerLevel = 5;
        private const int UndoPackAmount = 5;
        private const int UndoPackCost = 75;
        private const int RevealBaseCost = 15;

        private static readonly string[] BackgroundNames =
            { "CLASSIC", "MYSTIC LIBRARY", "MOON OBSERVATORY", "ENCHANTED GARDEN", "NEON ALCHEMY" };

        private static readonly int[] BackgroundCosts = { 0, 150, 225, 300, 400 };

        private const float LiftDuration = 0.18f;
        private const float TravelDuration = 0.24f;
        private const float DropDuration = 0.22f;

        private static readonly Color Background = new Color32(18, 17, 57, 255);
        private static readonly Color BackgroundLight = new Color32(34, 28, 87, 255);
        private static readonly Color Panel = new Color32(52, 35, 112, 245);
        private static readonly Color PanelLight = new Color32(73, 47, 145, 255);
        private static readonly Color Purple = new Color32(132, 69, 224, 255);
        private static readonly Color Blue = new Color32(31, 139, 242, 255);
        private static readonly Color Green = new Color32(102, 185, 41, 255);
        private static readonly Color Cyan = new Color32(72, 215, 255, 255);
        private static readonly Color Gold = new Color32(255, 205, 65, 255);
        private static readonly Color Muted = new Color32(185, 181, 218, 255);
        private static readonly Color Danger = new Color32(255, 111, 142, 255);

        private void Awake()
        {
            DOTween.Init(false, true, LogBehaviour.ErrorsOnly);
            monetization = GetComponent<MonetizationManager>();
            if (monetization == null) monetization = gameObject.AddComponent<MonetizationManager>();
            monetization.StatusChanged += OnMonetizationStatus;
            monetization.ProductsChanged += RefreshShopPrices;
            monetization.BalancesChanged += OnMonetizationBalancesChanged;
            monetization.InitializationProgressChanged += OnInitializationProgress;
            ConfigureCanvas();
            ConfigureAudio();
            LoadBackgroundAssets();
            LoadInterfaceIcons();
            CreateRoundedButtonSprite();
            BuildInterface();
            ApplySelectedBackground();
            ShowMainMenu();
        }

        private void Start()
        {
            if (monetization.HasConsentChoice) BeginStartupLoading();
            else ShowPrivacyChoices();
        }

        private void OnDestroy()
        {
            if (monetization != null)
            {
                monetization.StatusChanged -= OnMonetizationStatus;
                monetization.ProductsChanged -= RefreshShopPrices;
                monetization.BalancesChanged -= OnMonetizationBalancesChanged;
                monetization.InitializationProgressChanged -= OnInitializationProgress;
            }
            if (startupRoutine != null) StopCoroutine(startupRoutine);
            if (gameRoot != null) DOTween.Kill(gameRoot, false);
            if (loadingPanel != null) DOTween.Kill(loadingPanel, false);
            if (roundedButtonSprite != null) Destroy(roundedButtonSprite);
            if (roundedButtonTexture != null) Destroy(roundedButtonTexture);
            if (settingsIcon != null) Destroy(settingsIcon);
            if (backIcon != null) Destroy(backIcon);
            if (undoIcon != null) Destroy(undoIcon);
            if (resetIcon != null) Destroy(resetIcon);
            if (gameLogo != null) Destroy(gameLogo);
        }

        private void ConfigureCanvas()
        {
            Canvas canvas = GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                gameObject.AddComponent<GraphicRaycaster>();
            }

            CanvasScaler scaler = GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            // Expand always keeps the complete 1080x1920 design area visible.
            // Wider/tablet portraits receive extra horizontal space and tall phones
            // receive extra vertical space instead of cropping gameplay controls.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            Transform oldContent = transform.Find("Game View");
            if (oldContent != null) oldContent.gameObject.SetActive(false);
            Transform previous = transform.Find("Word Sort Game");
            if (previous != null) Destroy(previous.gameObject);
        }

        private void ConfigureAudio()
        {
            uiAudio = gameObject.AddComponent<AudioSource>();
            uiAudio.playOnAwake = false;
            uiAudio.volume = 0.22f;
            musicAudio = gameObject.AddComponent<AudioSource>();
            musicAudio.playOnAwake = false;
            musicAudio.loop = true;
            musicAudio.volume = 0.055f;
            clickClip = CreateTone("UI Click", 520f, 0.07f, 0.18f);
            successClip = CreateSuccessTone();
        }

        private void BuildInterface()
        {
            gameRoot = CreateRect("Word Sort Game", transform);
            Stretch(gameRoot);
            sceneBackground = AddImage("Background", gameRoot, null, Background);
            Stretch(sceneBackground.rectTransform);
            sceneBackground.raycastTarget = false;
            backgroundShade = AddImage("Background Shade", gameRoot, null, new Color32(7, 5, 28, 0));
            Stretch(backgroundShade.rectTransform);
            backgroundShade.raycastTarget = false;
            AddDecoration(new Vector2(-430, 610), new Vector2(560, 560), new Color32(20, 140, 190, 24));
            AddDecoration(new Vector2(490, 240), new Vector2(680, 680), new Color32(185, 48, 178, 22));
            AddDecoration(new Vector2(0, -820), new Vector2(1040, 440), new Color32(97, 54, 170, 30));

            mainScreen = CreateRect("Main Menu", gameRoot);
            Stretch(mainScreen);
            BuildMainMenu();
            levelScreen = CreateRect("Level Select", gameRoot);
            Stretch(levelScreen);
            BuildLevelSelect();
            playScreen = CreateRect("Play Screen", gameRoot);
            Stretch(playScreen);
            BuildPlayScreen();
            backgroundScreen = CreateRect("Background Select", gameRoot);
            Stretch(backgroundScreen);
            BuildBackgroundScreen();
            BuildSettingsPanel();
            BuildShopPanel();
            BuildWinPanel();
            BuildLoadingPanel();
            BuildPrivacyPanel();
        }

        private void BuildMainMenu()
        {
            TextMeshProUGUI eyebrow = AddText("Eyebrow", mainScreen, "A RELAXING WORD PUZZLE", 30, FontStyles.Bold);
            SetRect(eyebrow.rectTransform, new Vector2(0, 720), new Vector2(800, 55));
            eyebrow.color = Cyan;

            if (gameLogo != null)
            {
                Image logo = AddImage("Word Sort Logo", mainScreen, gameLogo, Color.white);
                SetRect(logo.rectTransform, new Vector2(0, 390), new Vector2(820, 615));
                logo.preserveAspect = true;
                logo.raycastTarget = false;
            }
            else
            {
                TextMeshProUGUI fallbackTitle = AddText("Logo Fallback", mainScreen, "WORD\nSORT", 132,
                    FontStyles.Bold);
                SetRect(fallbackTitle.rectTransform, new Vector2(0, 390), new Vector2(880, 290));
            }

            Button play = CreateButton(mainScreen, "PLAY", new Vector2(0, -120), new Vector2(600, 130), Purple, 49);
            play.onClick.AddListener(() => LoadLevel(enableLevelTesting
                ? Mathf.Clamp(testLevelNumber - 1, 0, WordLevelCatalog.TotalLevels - 1)
                : GameProgress.CurrentLevel));
            Button levels = CreateButton(mainScreen, "LEVELS", new Vector2(0, -285), new Vector2(600, 115), Blue, 42);
            levels.onClick.AddListener(ShowLevelSelect);
            Button shop = CreateButton(mainScreen, "SHOP", new Vector2(0, -425), new Vector2(600, 105), Gold, 38);
            shop.onClick.AddListener(ShowShop);
            Button backgrounds = CreateButton(mainScreen, "BACKGROUNDS", new Vector2(0, -555),
                new Vector2(600, 100), Green, 35);
            backgrounds.onClick.AddListener(ShowBackgroundSelect);
            Button settings = CreateButton(mainScreen, "SETTINGS", new Vector2(0, -675), new Vector2(600, 90),
                PanelLight, 38);
            settings.onClick.AddListener(ShowSettings);
            mainProgressText = AddText("Progress", mainScreen, string.Empty, 29, FontStyles.Normal);
            SetRect(mainProgressText.rectTransform, new Vector2(0, -775), new Vector2(850, 55));
            mainProgressText.color = Muted;
            CreateCoinBadge(mainScreen, new Vector2(365, 755), true);
            TextMeshProUGUI footer = AddText("Footer", mainScreen, "200 LEVELS  •  3–6 LETTER WORDS", 25,
                FontStyles.Bold);
            SetRect(footer.rectTransform, new Vector2(0, -855), new Vector2(900, 50));
            footer.color = new Color32(133, 124, 181, 255);
        }

        private void BuildLevelSelect()
        {
            Button back = CreateButton(levelScreen, "‹", new Vector2(-450, 820), new Vector2(105, 85), PanelLight, 58);
            back.onClick.AddListener(ShowMainMenu);
            TextMeshProUGUI title = AddText("Title", levelScreen, "SELECT LEVEL", 58, FontStyles.Bold);
            SetRect(title.rectTransform, new Vector2(0, 820), new Vector2(650, 90));
            /*Button settings = CreateButton(levelScreen, "SET", new Vector2(450, 820), new Vector2(105, 85), PanelLight,
                28);
            settings.onClick.AddListener(ShowSettings);*/
            //CreateCoinBadge(levelScreen, new Vector2(315, 820), true);
            TextMeshProUGUI groups = AddText("Groups", levelScreen,
                "1–50: 3 LETTERS   •   51–100: 4   •   101–150: 5   •   151–200: 6", 24, FontStyles.Bold);
            SetRect(groups.rectTransform, new Vector2(0, 730), new Vector2(1000, 55));
            groups.color = Muted;

            Image viewportImage = AddImage("Viewport", levelScreen, null, new Color32(35, 28, 82, 210));
            SetRect(viewportImage.rectTransform, new Vector2(0, -80), new Vector2(970, 1510));
            Mask mask = viewportImage.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;
            ScrollRect scroll = viewportImage.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 45f;

            RectTransform content = CreateRect("Content", viewportImage.rectTransform);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = new Vector2(0, -25);
            content.sizeDelta = new Vector2(-36, 6650);
            scroll.viewport = viewportImage.rectTransform;
            scroll.content = content;
            GridLayoutGroup grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(205, 110);
            grid.spacing = new Vector2(20, 20);
            grid.padding = new RectOffset(22, 22, 12, 24);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 4;
            grid.childAlignment = TextAnchor.UpperCenter;

            for (int i = 0; i < WordLevelCatalog.TotalLevels; i++)
            {
                int captured = i;
                Button button = CreateLayoutButton(content, i + 1);
                TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
                button.onClick.AddListener(() => LoadLevel(captured));
                levelButtons.Add(new LevelButtonView(i, button, label, button.GetComponent<Image>()));
            }
        }

        private void BuildPlayScreen()
        {
            Button menu = CreateButton(playScreen, "‹", new Vector2(-465, 885), new Vector2(92, 82), PanelLight, 58);
            menu.onClick.AddListener(ShowMainMenu);
            levelTitleText = AddText("Level", playScreen, "LEVEL 1", 47, FontStyles.Bold);
            SetRect(levelTitleText.rectTransform, new Vector2(0, 887), new Vector2(610, 80));
            Button settings = CreateButton(playScreen, "SET", new Vector2(465, 885), new Vector2(92, 82), PanelLight,
                26);
            settings.onClick.AddListener(ShowSettings);
            CreateCoinBadge(playScreen, new Vector2(300, 885), true);
            TextMeshProUGUI goal = AddText("Goal", playScreen, "FIND THE WORDS", 30, FontStyles.Bold);
            SetRect(goal.rectTransform, new Vector2(0, 795), new Vector2(600, 55));
            goal.color = Muted;
            targetContainer = CreateRect("Targets", playScreen);
            SetRect(targetContainer, new Vector2(0, 690), new Vector2(1020, 125));
            helperText = AddText("Helper", playScreen, "SELECT A TOP LETTER", 31, FontStyles.Bold);
            SetRect(helperText.rectTransform, new Vector2(0, 575), new Vector2(950, 55));
            helperText.color = Muted;
            boardHost = CreateRect("Board Host", playScreen);
            SetRect(boardHost, new Vector2(0, -25), new Vector2(1060, 1140));
            // Keep all gameplay actions in one balanced row and leave the bottom safe area for the ad banner.
            moveText = AddText("Moves", playScreen, "MOVES: 0", 31, FontStyles.Bold);
            SetRect(moveText.rectTransform, new Vector2(0, -735), new Vector2(310, 112));
            moveText.color = Color.white;

            Button undo = CreateButton(playScreen, "UNDO", new Vector2(-275, -735), new Vector2(112, 112),
                PanelLight, 24);
            undoButtonText = undo.GetComponentInChildren<TextMeshProUGUI>();
            undo.onClick.AddListener(UndoLastMove);
            Button reset = CreateButton(playScreen, "RESET", new Vector2(275, -735), new Vector2(112, 112), Purple,
                24);
            reset.onClick.AddListener(ResetLevel);
        }

        private void LoadBackgroundAssets()
        {
            backgroundSprites = new Sprite[BackgroundNames.Length];
            backgroundSprites[1] = Resources.Load<Sprite>("WordSortBackgrounds/mystic-library");
            backgroundSprites[2] = Resources.Load<Sprite>("WordSortBackgrounds/moon-observatory");
            backgroundSprites[3] = Resources.Load<Sprite>("WordSortBackgrounds/enchanted-garden");
            backgroundSprites[4] = Resources.Load<Sprite>("WordSortBackgrounds/neon-alchemy");
        }

        private void LoadInterfaceIcons()
        {
            settingsIcon = CreateRuntimeSprite("WordSortUI/settings", "Settings Icon");
            backIcon = CreateRuntimeSprite("WordSortUI/back", "Back Icon");
            undoIcon = CreateRuntimeSprite("WordSortUI/undo-generated", "Generated Undo Icon");
            resetIcon = CreateRuntimeSprite("WordSortUI/reset", "Reset Icon");
            // Crop the generated transparent canvas to its visible logo bounds.
            gameLogo = CreateRuntimeSprite("WordSortUI/word-sort-logo", "Word Sort Logo",
                new Rect(15f, 281f, 1169f, 876f));
        }

        private static Sprite CreateRuntimeSprite(string resourcePath, string spriteName, Rect? crop = null)
        {
            Texture2D texture = Resources.Load<Texture2D>(resourcePath);
            if (texture == null) return null;
            Rect spriteRect = crop ?? new Rect(0, 0, texture.width, texture.height);
            spriteRect.x = Mathf.Clamp(spriteRect.x, 0f, texture.width - 1f);
            spriteRect.y = Mathf.Clamp(spriteRect.y, 0f, texture.height - 1f);
            spriteRect.width = Mathf.Clamp(spriteRect.width, 1f, texture.width - spriteRect.x);
            spriteRect.height = Mathf.Clamp(spriteRect.height, 1f, texture.height - spriteRect.y);
            Sprite sprite = Sprite.Create(texture, spriteRect,
                new Vector2(0.5f, 0.5f), 100f);
            sprite.name = spriteName;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private void CreateRoundedButtonSprite()
        {
            const int textureSize = 64;
            const float radius = 15f;
            roundedButtonTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false)
            {
                name = "Runtime Rounded Button",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color32[] pixels = new Color32[textureSize * textureSize];
            Vector2 halfSize = Vector2.one * (textureSize * 0.5f - 1f);
            Vector2 inner = halfSize - Vector2.one * radius;
            for (int y = 0; y < textureSize; y++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    Vector2 point = new Vector2(x + 0.5f, y + 0.5f) - Vector2.one * (textureSize * 0.5f);
                    Vector2 corner = new Vector2(Mathf.Abs(point.x), Mathf.Abs(point.y)) - inner;
                    Vector2 outside = new Vector2(Mathf.Max(corner.x, 0f), Mathf.Max(corner.y, 0f));
                    float distance = outside.magnitude + Mathf.Min(Mathf.Max(corner.x, corner.y), 0f) - radius;
                    byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(0.5f - distance) * 255f);
                    pixels[y * textureSize + x] = new Color32(255, 255, 255, alpha);
                }
            }

            roundedButtonTexture.SetPixels32(pixels);
            roundedButtonTexture.Apply(false, true);
            roundedButtonSprite = Sprite.Create(roundedButtonTexture, new Rect(0, 0, textureSize, textureSize),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(16f, 16f, 16f, 16f));
            roundedButtonSprite.name = "Runtime Rounded Button";
            roundedButtonSprite.hideFlags = HideFlags.HideAndDontSave;
        }

        private void BuildBackgroundScreen()
        {
            Button back = CreateButton(backgroundScreen, "‹", new Vector2(-465, 865), new Vector2(92, 82),
                PanelLight, 58);
            back.onClick.AddListener(ShowMainMenu);
            TextMeshProUGUI title = AddText("Title", backgroundScreen, "BACKGROUNDS", 56, FontStyles.Bold);
            SetRect(title.rectTransform, new Vector2(0, 865), new Vector2(650, 85));
            CreateCoinBadge(backgroundScreen, new Vector2(390, 865), true);
            TextMeshProUGUI subtitle = AddText("Subtitle", backgroundScreen,
                "CHOOSE A THEME  •  UNLOCK NEW ONES WITH COINS", 26, FontStyles.Bold);
            SetRect(subtitle.rectTransform, new Vector2(0, 775), new Vector2(950, 55));
            subtitle.color = Muted;

            backgroundCards.Clear();
            for (int i = 0; i < BackgroundNames.Length; i++)
            {
                int captured = i;
                int row = i / 2;
                int column = i % 2;
                float x = column == 0 ? -245f : 245f;
                if (i == BackgroundNames.Length - 1 && BackgroundNames.Length % 2 == 1) x = 0f;
                float y = 505f - row * 505f;

                Image card = AddImage("Background " + BackgroundNames[i], backgroundScreen, null, Panel);
                SetRect(card.rectTransform, new Vector2(x, y), new Vector2(440, 455));
                Image previewWindow = AddImage("Preview", card.rectTransform, null, BackgroundLight);
                SetRect(previewWindow.rectTransform, new Vector2(0, 62), new Vector2(386, 280));
                Mask mask = previewWindow.gameObject.AddComponent<Mask>();
                mask.showMaskGraphic = true;
                Image preview = AddImage("Artwork", previewWindow.rectTransform, backgroundSprites[i],
                    i == 0 ? Background : Color.white);
                Stretch(preview.rectTransform);
                preview.raycastTarget = false;
                if (backgroundSprites[i] != null)
                {
                    AspectRatioFitter fitter = preview.gameObject.AddComponent<AspectRatioFitter>();
                    fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                    fitter.aspectRatio = backgroundSprites[i].rect.width / backgroundSprites[i].rect.height;
                }

                if (i == 0)
                {
                    TextMeshProUGUI classic = AddText("Classic", preview.rectTransform, "WORD\nSORT", 48,
                        FontStyles.Bold);
                    Stretch(classic.rectTransform, new Vector2(15, 10), new Vector2(-15, -10));
                    classic.color = Cyan;
                    classic.raycastTarget = false;
                }

                TextMeshProUGUI name = AddText("Name", card.rectTransform, BackgroundNames[i], 27,
                    FontStyles.Bold);
                SetRect(name.rectTransform, new Vector2(0, -105), new Vector2(390, 48));
                Button action = CreateButton(card.rectTransform, "USE", new Vector2(0, -170),
                    new Vector2(320, 68), i == 0 ? Blue : Purple, 25);
                action.onClick.AddListener(() => TrySelectBackground(captured));
                backgroundCards.Add(new BackgroundCardView(i, card, action,
                    action.GetComponentInChildren<TextMeshProUGUI>()));
            }
        }

        private void BuildSettingsPanel()
        {
            Image blocker = AddImage("Settings Overlay", gameRoot, null, new Color32(9, 7, 29, 225));
            Stretch(blocker.rectTransform);
            settingsPanel = blocker.gameObject.AddComponent<CanvasGroup>();
            Image card = AddImage("Card", blocker.rectTransform, null, Panel);
            SetRect(card.rectTransform, Vector2.zero, new Vector2(850, 930));
            TextMeshProUGUI title = AddText("Title", card.rectTransform, "SETTINGS", 65, FontStyles.Bold);
            SetRect(title.rectTransform, new Vector2(0, 315), new Vector2(600, 90));
            Button close = CreateButton(card.rectTransform, "×", new Vector2(350, 330), new Vector2(90, 80), PanelLight,
                55);
            close.onClick.AddListener(HideSettings);
            soundValueText = CreateSettingRow(card.rectTransform, "SOUND", 160, ToggleSound);
            musicValueText = CreateSettingRow(card.rectTransform, "BACKGROUND MUSIC", 25, ToggleMusic);
            vibrationValueText = CreateSettingRow(card.rectTransform, "VIBRATION", -110, ToggleVibration);
            Button privacy = CreateButton(card.rectTransform, "PRIVACY CHOICES", new Vector2(0, -245),
                new Vector2(520, 82), Blue, 28);
            privacy.onClick.AddListener(ShowPrivacyChoices);
            TextMeshProUGUI note = AddText("Note", card.rectTransform, "PROGRESS IS SAVED AUTOMATICALLY", 25,
                FontStyles.Bold);
            SetRect(note.rectTransform, new Vector2(0, -365), new Vector2(700, 60));
            note.color = Muted;
            HidePanelInstant(settingsPanel);
        }

        private void BuildShopPanel()
        {
            Image blocker = AddImage("Shop Overlay", gameRoot, null, new Color32(9, 7, 29, 232));
            Stretch(blocker.rectTransform);
            shopPanel = blocker.gameObject.AddComponent<CanvasGroup>();
            Image card = AddImage("Card", blocker.rectTransform, null, Panel);
            SetRect(card.rectTransform, Vector2.zero, new Vector2(900, 1560));
            TextMeshProUGUI title = AddText("Title", card.rectTransform, "COIN SHOP", 64, FontStyles.Bold);
            SetRect(title.rectTransform, new Vector2(0, 665), new Vector2(650, 90));
            Button close = CreateButton(card.rectTransform, "×", new Vector2(370, 690), new Vector2(86, 78), PanelLight,
                54);
            close.onClick.AddListener(HideShop);
            CreateCoinBadge(card.rectTransform, new Vector2(0, 570), false);

            shopUndoText = AddText("Undo Balance", card.rectTransform, string.Empty, 27, FontStyles.Bold);
            SetRect(shopUndoText.rectTransform, new Vector2(0, 495), new Vector2(500, 55));
            shopUndoText.color = Cyan;

            TextMeshProUGUI intro = AddText("Intro", card.rectTransform,
                "SECURE PURCHASES PROVIDED BY YOUR APP STORE", 25, FontStyles.Bold);
            SetRect(intro.rectTransform, new Vector2(0, 425), new Vector2(790, 55));
            intro.color = Muted;

            smallPackPriceText = CreateShopPack(card.rectTransform, monetization.Config.smallCoins,
                "SMALL PACK", monetization.Config.smallCoinsProductId, 325, Blue);
            mediumPackPriceText = CreateShopPack(card.rectTransform, monetization.Config.mediumCoins,
                "POPULAR PACK", monetization.Config.mediumCoinsProductId, 175, Green);
            largePackPriceText = CreateShopPack(card.rectTransform, monetization.Config.largeCoins,
                "BIG PACK", monetization.Config.largeCoinsProductId, 25, Purple);

            Image rewardedRow = AddImage("Rewarded Coins", card.rectTransform, null, BackgroundLight);
            SetRect(rewardedRow.rectTransform, new Vector2(0, -125), new Vector2(760, 130));
            TextMeshProUGUI rewardedLabel = AddText("Label", rewardedRow.rectTransform,
                $"FREE COINS\n+{monetization.Config.rewardedCoins} COINS", 29, FontStyles.Bold);
            SetRect(rewardedLabel.rectTransform, new Vector2(-170, 0), new Vector2(390, 100));
            rewardedLabel.alignment = TextAlignmentOptions.MidlineLeft;
            Button watchAd = CreateButton(rewardedRow.rectTransform, "WATCH AD", new Vector2(235, 0),
                new Vector2(245, 78), Cyan, 24);
            watchAd.onClick.AddListener(monetization.ShowRewardedCoins);

            Image noAdsRow = AddImage("Remove Ads", card.rectTransform, null, BackgroundLight);
            SetRect(noAdsRow.rectTransform, new Vector2(0, -270), new Vector2(760, 130));
            TextMeshProUGUI noAdsLabel = AddText("Label", noAdsRow.rectTransform, "REMOVE ADS\nPERMANENT",
                29, FontStyles.Bold);
            SetRect(noAdsLabel.rectTransform, new Vector2(-170, 0), new Vector2(390, 100));
            noAdsLabel.alignment = TextAlignmentOptions.MidlineLeft;
            Button noAds = CreateButton(noAdsRow.rectTransform, "BUY", new Vector2(235, 0),
                new Vector2(245, 78), Purple, 24);
            removeAdsPriceText = noAds.GetComponentInChildren<TextMeshProUGUI>();
            noAds.onClick.AddListener(() => monetization.BuyProduct(monetization.Config.removeAdsProductId));

            Image undoRow = AddImage("Undo Pack", card.rectTransform, null, BackgroundLight);
            SetRect(undoRow.rectTransform, new Vector2(0, -415), new Vector2(760, 130));
            TextMeshProUGUI undoLabel = AddText("Label", undoRow.rectTransform, $"UNDO PACK\n+{UndoPackAmount} UNDO",
                31, FontStyles.Bold);
            SetRect(undoLabel.rectTransform, new Vector2(-170, 0), new Vector2(390, 105));
            undoLabel.alignment = TextAlignmentOptions.MidlineLeft;
            Button buyUndo = CreateButton(undoRow.rectTransform, $"{UndoPackCost} COINS", new Vector2(235, 0),
                new Vector2(245, 82), Gold, 25);
            buyUndo.onClick.AddListener(BuyUndoPack);

            shopStatusText = AddText("Status", card.rectTransform, string.Empty, 25, FontStyles.Bold);
            SetRect(shopStatusText.rectTransform, new Vector2(0, -520), new Vector2(790, 55));
            shopStatusText.enableAutoSizing = true;
            shopStatusText.fontSizeMin = 16f;
            shopStatusText.fontSizeMax = 25f;

            Button restore = CreateButton(card.rectTransform, "RESTORE PURCHASES", new Vector2(0, -610),
                new Vector2(440, 72), PanelLight, 23);
            restore.onClick.AddListener(monetization.RestorePurchases);
            TextMeshProUGUI note = AddText("Note", card.rectTransform, "PURCHASES ARE SUBJECT TO STORE TERMS",
                23, FontStyles.Bold);
            SetRect(note.rectTransform, new Vector2(0, -690), new Vector2(790, 45));
            note.color = Gold;
            HidePanelInstant(shopPanel);
        }

        private void BuildLoadingPanel()
        {
            Image blocker = AddImage("Loading Screen", gameRoot, null, new Color32(13, 10, 42, 255));
            Stretch(blocker.rectTransform);
            loadingPanel = blocker.gameObject.AddComponent<CanvasGroup>();

            if (gameLogo != null)
            {
                Image logo = AddImage("Logo", blocker.rectTransform, gameLogo, Color.white);
                SetRect(logo.rectTransform, new Vector2(0, 340), new Vector2(650, 488));
                logo.preserveAspect = true;
                logo.raycastTarget = false;
            }
            else
            {
                TextMeshProUGUI title = AddText("Title", blocker.rectTransform, "WORD SORT", 82, FontStyles.Bold);
                SetRect(title.rectTransform, new Vector2(0, 330), new Vector2(800, 120));
            }

            loadingSpinner = CreateRect("Spinner", blocker.rectTransform);
            SetRect(loadingSpinner, new Vector2(0, 20), new Vector2(230, 230));
            Color[] colors = { Blue, Green, Gold, Purple, Cyan, Blue, Green, Gold };
            for (int i = 0; i < 8; i++)
            {
                float angle = i * 45f;
                Image bar = AddImage("Spinner Bar " + i, loadingSpinner, null, colors[i]);
                SetRect(bar.rectTransform,
                    new Vector2(Mathf.Sin(angle * Mathf.Deg2Rad), Mathf.Cos(angle * Mathf.Deg2Rad)) * 88f,
                    new Vector2(22, 58));
                bar.rectTransform.localRotation = Quaternion.Euler(0, 0, -angle);
                bar.raycastTarget = false;
            }

            string[] letters = { "W", "O", "R", "D" };
            Color[] tileColors = { Blue, Gold, Green, Purple };
            for (int i = 0; i < letters.Length; i++)
            {
                Image tile = AddImage("Loading Tile " + letters[i], blocker.rectTransform, null, tileColors[i]);
                SetRect(tile.rectTransform, new Vector2((i - 1.5f) * 92f, -215), new Vector2(76, 74));
                TextMeshProUGUI letter = AddText("Letter", tile.rectTransform, letters[i], 45, FontStyles.Bold);
                Stretch(letter.rectTransform, new Vector2(5, 3), new Vector2(-5, -3));
                letter.raycastTarget = false;
                loadingTiles.Add(tile.rectTransform);
            }

            loadingStatusText = AddText("Loading Status", blocker.rectTransform, "PREPARING GAME...", 29,
                FontStyles.Bold);
            SetRect(loadingStatusText.rectTransform, new Vector2(0, -345), new Vector2(820, 60));
            loadingStatusText.color = Muted;

            Image progressTrack = AddImage("Progress Track", blocker.rectTransform, roundedButtonSprite,
                new Color32(54, 43, 101, 255));
            progressTrack.type = Image.Type.Sliced;
            SetRect(progressTrack.rectTransform, new Vector2(0, -420), new Vector2(650, 28));
            progressTrack.raycastTarget = false;
            loadingProgressFill = AddImage("Progress Fill", progressTrack.rectTransform, roundedButtonSprite, Cyan);
            loadingProgressFill.type = Image.Type.Sliced;
            loadingProgressFill.rectTransform.anchorMin = Vector2.zero;
            loadingProgressFill.rectTransform.anchorMax = new Vector2(0.02f, 1f);
            loadingProgressFill.rectTransform.pivot = new Vector2(0f, 0.5f);
            loadingProgressFill.rectTransform.offsetMin = Vector2.zero;
            loadingProgressFill.rectTransform.offsetMax = Vector2.zero;
            loadingProgressFill.raycastTarget = false;

            TextMeshProUGUI systems = AddText("Systems", blocker.rectTransform,
                "LEVELS  •  AUDIO  •  PURCHASES  •  ADS", 21, FontStyles.Bold);
            SetRect(systems.rectTransform, new Vector2(0, -475), new Vector2(850, 45));
            systems.color = new Color32(130, 124, 174, 255);
            HidePanelInstant(loadingPanel);
        }

        private void BuildPrivacyPanel()
        {
            Image blocker = AddImage("Privacy Overlay", gameRoot, null, new Color32(9, 7, 29, 248));
            Stretch(blocker.rectTransform);
            privacyPanel = blocker.gameObject.AddComponent<CanvasGroup>();
            Image card = AddImage("Card", blocker.rectTransform, null, Panel);
            SetRect(card.rectTransform, Vector2.zero, new Vector2(900, 1180));

            TextMeshProUGUI title = AddText("Title", card.rectTransform, "YOUR PRIVACY", 61, FontStyles.Bold);
            SetRect(title.rectTransform, new Vector2(0, 465), new Vector2(760, 90));
            title.color = Gold;

            TextMeshProUGUI body = AddText("Explanation", card.rectTransform,
                "This game and its advertising partners may use device identifiers and app activity " +
                "to provide ads, measure performance, prevent fraud, and improve the game.\n\n" +
                "Choose personalized ads or non-personalized ads. You can change this later in Settings. " +
                "The game remains playable either way.", 29, FontStyles.Normal);
            SetRect(body.rectTransform, new Vector2(0, 155), new Vector2(760, 460));
            body.textWrappingMode = TextWrappingModes.Normal;
            body.alignment = TextAlignmentOptions.TopLeft;
            body.color = Muted;

            Button personalized = CreateButton(card.rectTransform, "ALLOW PERSONALIZED ADS",
                new Vector2(0, -170), new Vector2(690, 105), Purple, 30);
            personalized.onClick.AddListener(() => ChoosePrivacy(true));
            Button nonPersonalized = CreateButton(card.rectTransform, "USE NON-PERSONALIZED ADS",
                new Vector2(0, -305), new Vector2(690, 100), Blue, 28);
            nonPersonalized.onClick.AddListener(() => ChoosePrivacy(false));
            Button policy = CreateButton(card.rectTransform, "READ PRIVACY POLICY",
                new Vector2(0, -430), new Vector2(520, 80), PanelLight, 24);
            policy.onClick.AddListener(monetization.OpenPrivacyPolicy);

            TextMeshProUGUI footnote = AddText("Footnote", card.rectTransform,
                "This native game does not use browser cookies. Ad SDKs may use mobile identifiers.",
                21, FontStyles.Normal);
            SetRect(footnote.rectTransform, new Vector2(0, -520), new Vector2(760, 55));
            footnote.textWrappingMode = TextWrappingModes.Normal;
            footnote.color = Muted;
            HidePanelInstant(privacyPanel);
        }

        private void ShowPrivacyChoices()
        {
            HidePanelInstant(settingsPanel);
            ShowPanel(privacyPanel);
            privacyPanel.transform.SetAsLastSibling();
        }

        private void ChoosePrivacy(bool personalized)
        {
            monetization.SaveConsentChoice(personalized);
            HidePanel(privacyPanel);
            if (!monetization.InitializationStarted) BeginStartupLoading();
        }

        private TextMeshProUGUI CreateShopPack(Transform parent, int amount, string title, string productId,
            float y, Color color)
        {
            Image row = AddImage(title, parent, null, BackgroundLight);
            SetRect(row.rectTransform, new Vector2(0, y), new Vector2(760, 140));
            TextMeshProUGUI label = AddText("Label", row.rectTransform, $"{title}\n+{amount} COINS", 31,
                FontStyles.Bold);
            SetRect(label.rectTransform, new Vector2(-170, 0), new Vector2(390, 105));
            label.alignment = TextAlignmentOptions.MidlineLeft;
            Button buy = CreateButton(row.rectTransform, "BUY", new Vector2(235, 0), new Vector2(245, 82), color,
                25);
            buy.onClick.AddListener(() => monetization.BuyProduct(productId));
            return buy.GetComponentInChildren<TextMeshProUGUI>();
        }

        private void BuildWinPanel()
        {
            Image blocker = AddImage("Win Overlay", gameRoot, null, new Color32(9, 7, 29, 232));
            Stretch(blocker.rectTransform);
            winPanel = blocker.gameObject.AddComponent<CanvasGroup>();
            Image card = AddImage("Card", blocker.rectTransform, null, Panel);
            SetRect(card.rectTransform, Vector2.zero, new Vector2(870, 880));
            TextMeshProUGUI star = AddText("Star", card.rectTransform, "WIN", 72, FontStyles.Bold);
            SetRect(star.rectTransform, new Vector2(0, 290), new Vector2(300, 150));
            star.color = Gold;
            winTitleText = AddText("Title", card.rectTransform, "LEVEL COMPLETE!", 62, FontStyles.Bold);
            SetRect(winTitleText.rectTransform, new Vector2(0, 145), new Vector2(760, 95));
            winStatsText = AddText("Stats", card.rectTransform, "", 31, FontStyles.Normal);
            SetRect(winStatsText.rectTransform, new Vector2(0, 15), new Vector2(720, 145));
            winStatsText.color = Muted;
            Button next = CreateButton(card.rectTransform, "NEXT LEVEL", new Vector2(0, -155), new Vector2(560, 120),
                Purple, 42);
            nextButtonText = next.GetComponentInChildren<TextMeshProUGUI>();
            next.onClick.AddListener(NextLevel);
            Button levels = CreateButton(card.rectTransform, "LEVEL SELECT", new Vector2(0, -305),
                new Vector2(560, 100), PanelLight, 34);
            levels.onClick.AddListener(ShowLevelSelect);
            HidePanelInstant(winPanel);
        }

        private void ShowMainMenu()
        {
            StopGameplayTweens();
            mainScreen.gameObject.SetActive(true);
            levelScreen.gameObject.SetActive(false);
            playScreen.gameObject.SetActive(false);
            backgroundScreen.gameObject.SetActive(false);
            HidePanelInstant(winPanel);
            HidePanelInstant(settingsPanel);
            HidePanelInstant(shopPanel);
            int completed = Enumerable.Range(0, WordLevelCatalog.TotalLevels).Count(GameProgress.IsCompleted);
            mainProgressText.text = $"CURRENT LEVEL: {GameProgress.CurrentLevel + 1}   •   COMPLETED: {completed}/200";
            RefreshCoinText();
            ApplyAudioSettings();
            monetization?.SetBannerVisible(true);
        }

        private void ShowLevelSelect()
        {
            StopGameplayTweens();
            mainScreen.gameObject.SetActive(false);
            levelScreen.gameObject.SetActive(true);
            playScreen.gameObject.SetActive(false);
            backgroundScreen.gameObject.SetActive(false);
            HidePanelInstant(winPanel);
            HidePanelInstant(settingsPanel);
            HidePanelInstant(shopPanel);
            RefreshLevelButtons();
            RefreshCoinText();
            monetization?.SetBannerVisible(true);
        }

        private void ShowBackgroundSelect()
        {
            StopGameplayTweens();
            mainScreen.gameObject.SetActive(false);
            levelScreen.gameObject.SetActive(false);
            playScreen.gameObject.SetActive(false);
            backgroundScreen.gameObject.SetActive(true);
            HidePanelInstant(winPanel);
            HidePanelInstant(settingsPanel);
            HidePanelInstant(shopPanel);
            RefreshBackgroundCards();
            RefreshCoinText();
            monetization?.SetBannerVisible(true);
        }

        private void TrySelectBackground(int backgroundIndex)
        {
            if (backgroundIndex < 0 || backgroundIndex >= BackgroundNames.Length) return;
            if (!GameProgress.IsBackgroundUnlocked(backgroundIndex))
            {
                int cost = BackgroundCosts[backgroundIndex];
                if (!GameProgress.TrySpendCoins(cost))
                {
                    ShowShop();
                    return;
                }

                GameProgress.UnlockBackground(backgroundIndex);
            }

            GameProgress.SelectedBackground = backgroundIndex;
            ApplySelectedBackground();
            RefreshBackgroundCards();
            RefreshCoinText();
            sceneBackground.rectTransform.DOKill();
            sceneBackground.rectTransform.DOPunchScale(Vector3.one * 0.018f, 0.42f, 4, 0.5f);
        }

        private void RefreshBackgroundCards()
        {
            int selected = Mathf.Clamp(GameProgress.SelectedBackground, 0, BackgroundNames.Length - 1);
            foreach (BackgroundCardView view in backgroundCards)
            {
                bool unlocked = GameProgress.IsBackgroundUnlocked(view.Index);
                bool isSelected = selected == view.Index;
                view.Label.text = isSelected ? "SELECTED" : unlocked ? "USE" : $"{BackgroundCosts[view.Index]} COINS";
                view.Button.interactable = !isSelected;
                view.Card.color = isSelected ? new Color32(75, 54, 145, 255) : Panel;
            }
        }

        private void ApplySelectedBackground()
        {
            if (sceneBackground == null || backgroundSprites == null) return;
            int selected = Mathf.Clamp(GameProgress.SelectedBackground, 0, backgroundSprites.Length - 1);
            if (!GameProgress.IsBackgroundUnlocked(selected)) selected = 0;
            sceneBackground.sprite = backgroundSprites[selected];
            sceneBackground.color = selected == 0 || backgroundSprites[selected] == null ? Background : Color.white;
            backgroundShade.color = selected == 0
                ? new Color32(7, 5, 28, 0)
                : new Color32(7, 5, 28, 92);
        }

        private void LoadLevel(int levelIndex)
        {
            if (isLoading || (!enableLevelTesting && levelIndex > GameProgress.HighestUnlocked)) return;
            monetization?.SetBannerVisible(true);
            int requestedLevel = Mathf.Clamp(levelIndex, 0, WordLevelCatalog.TotalLevels - 1);
            ShowLoading(() =>
            {
                currentLevelIndex = requestedLevel;
                if (!enableLevelTesting) GameProgress.CurrentLevel = currentLevelIndex;
                level = WordLevelCatalog.Get(currentLevelIndex);
                initialStacks = GenerateScrambledStacks(level);
                ConfigureHiddenWords();
                freeUndosRemaining = FreeUndosPerLevel;
                mainScreen.gameObject.SetActive(false);
                levelScreen.gameObject.SetActive(false);
                playScreen.gameObject.SetActive(true);
                backgroundScreen.gameObject.SetActive(false);
                HidePanelInstant(winPanel);
                HidePanelInstant(settingsPanel);
                HidePanelInstant(shopPanel);
                RefreshCoinText();
                RefreshUndoText();
                BuildCurrentLevelBoard(initialStacks);
            }, 0.72f);
        }

        private void BuildCurrentLevelBoard(IReadOnlyList<List<char>> stacks)
        {
            StopGameplayTweens();
            if (boardContent != null)
            {
                boardContent.gameObject.SetActive(false);
                Destroy(boardContent.gameObject);
            }

            boardContent = CreateRect("Level " + level.Number, boardHost);
            Stretch(boardContent);
            tubes.Clear();
            bonusTube = null;
            undoStack.Clear();
            selectedTube = null;
            isAnimating = false;
            hasWon = false;
            moves = 0;
            completionReward = 0;
            levelTitleText.text = $"LEVEL {level.Number}";
            helperText.text = "SELECT A TOP LETTER";
            helperText.color = Muted;
            UpdateMoveText();
            RefreshUndoText();
            BuildTargets();

            Image boardBackground = AddImage("Board Background", boardContent, roundedButtonSprite,
                new Color32(61, 39, 125, 205));
            boardBackground.type = Image.Type.Sliced;
            SetRect(boardBackground.rectTransform, Vector2.zero, new Vector2(1040, 1120));
            boardBackground.raycastTarget = false;

            // The tube and its slots share one layout calculation. This keeps every
            // tile completely inside the procedural rails at every word/tube count.
            const float usableWidth = 960f;
            const float horizontalGap = 18f;
            const float verticalGap = 30f;
            // Six tubes still fit comfortably on one row. Starting at seven tubes,
            // split the board so the rails and letters stay large and easy to tap:
            // seven becomes 4 + 3, eight becomes 4 + 4, and so on.
            int totalTubeCount = level.TubeCount + 1;
            float oneRowTubeWidth = (usableWidth - horizontalGap * (totalTubeCount - 1)) / totalTubeCount;
            float oneRowRail = Mathf.Clamp(oneRowTubeWidth * 0.075f, 9f, 15f);
            float oneRowTileSize = oneRowTubeWidth - oneRowRail * 2f - 14f;
            bool useTwoRows = totalTubeCount >= 7 || oneRowTileSize < 72f;
            int firstRowCount = useTwoRows ? (totalTubeCount + 1) / 2 : totalTubeCount;
            int secondRowCount = useTwoRows ? totalTubeCount - firstRowCount : 0;
            int maximumColumns = Mathf.Max(firstRowCount, secondRowCount);
            float tubeWidth = Mathf.Min(220f, (usableWidth - horizontalGap * (maximumColumns - 1)) / maximumColumns);
            float railThickness = Mathf.Clamp(tubeWidth * 0.075f, 9f, 15f);
            float tileSize = Mathf.Min(154f, tubeWidth - railThickness * 2f - 14f);
            float tileGap = Mathf.Clamp(tileSize * 0.055f, 5f, 9f);
            float bottomThickness = Mathf.Clamp(railThickness * 1.9f, 22f, 30f);
            const float bottomPadding = 7f;
            const float topPadding = 25f;
            float maximumTubeHeight = useTwoRows ? (1040f - verticalGap) * 0.5f : 990f;
            float verticalTileLimit =
                (maximumTubeHeight - bottomThickness - bottomPadding - topPadding - (level.WordLength - 1) * tileGap) /
                level.WordLength;
            tileSize = Mathf.Min(tileSize, verticalTileLimit);
            // Tighten the tube after the vertical constraint is known. Without this,
            // short words on two rows leave a large, uneven gap beside each tile.
            railThickness = Mathf.Clamp((tileSize + 14f) * 0.088f, 9f, 15f);
            tubeWidth = tileSize + railThickness * 2f + 14f;
            bottomThickness = Mathf.Clamp(railThickness * 1.9f, 22f, 30f);
            tileGap = Mathf.Clamp(tileSize * 0.055f, 5f, 9f);
            float tubeHeight = bottomThickness + bottomPadding + level.WordLength * tileSize +
                               (level.WordLength - 1) * tileGap + topPadding;
            float spacingX = tubeWidth + horizontalGap;
            float slotSpacing = tileSize + tileGap;
            for (int i = 0; i < totalTubeCount; i++)
            {
                int row = useTwoRows && i >= firstRowCount ? 1 : 0;
                int indexInRow = row == 0 ? i : i - firstRowCount;
                int countInRow = row == 0 ? firstRowCount : secondRowCount;
                float x = (indexInRow - (countInRow - 1) * 0.5f) * spacingX;
                float y = useTwoRows
                    ? (row == 0 ? (tubeHeight + verticalGap) * 0.5f : -(tubeHeight + verticalGap) * 0.5f)
                    : -25f;
                CreateTube(i, new Vector2(x, y), new Vector2(tubeWidth, tubeHeight), tileSize, slotSpacing,
                    railThickness, bottomThickness, bottomPadding, i == totalTubeCount - 1);
            }

            piecesLayer = CreateRect("Pieces", boardContent);
            Stretch(piecesLayer);
            int visual = 0;
            for (int tubeIndex = 0; tubeIndex < stacks.Count; tubeIndex++)
            {
                foreach (char letter in stacks[tubeIndex])
                {
                    LetterTile tile = CreateLetterTile(letter, visual++, tileSize);
                    tubes[tubeIndex].Tiles.Add(tile);
                    tile.Rect.anchoredPosition = tubes[tubeIndex].SlotPosition(tubes[tubeIndex].Tiles.Count - 1);
                    tile.Rect.localScale = Vector3.zero;
                    tile.Rect.DOScale(1f, 0.28f).SetDelay(visual * 0.025f).SetEase(Ease.OutBack).SetTarget(gameRoot);
                }
            }

            EvaluateCompletedTubes(false);
        }

        private void BuildTargets()
        {
            foreach (Transform child in targetContainer) Destroy(child.gameObject);
            targetChips.Clear();
            float totalWidth = 980f;
            float spacing = totalWidth / level.WordCount;
            float width = Mathf.Min(280f, spacing - 16f);
            for (int i = 0; i < level.WordCount; i++)
            {
                float x = -totalWidth * 0.5f + spacing * (i + 0.5f);
                Color color = i % 4 == 0 ? Blue :
                    i % 4 == 1 ? Green :
                    i % 4 == 2 ? new Color32(229, 143, 31, 255) : Purple;
                bool hidden = hiddenWordIndices.Contains(i) && !GameProgress.IsWordRevealed(currentLevelIndex, i);
                targetChips.Add(CreateTargetChip(level.Words[i], i, hidden, WordRevealCost,
                    new Vector2(x, 0), new Vector2(width, 86), color));
            }
        }

        private int WordRevealCost => RevealBaseCost + (level.WordLength - 3) * 5;

        private void ConfigureHiddenWords()
        {
            hiddenWordIndices.Clear();
            int desiredHidden = currentLevelIndex < 10 ? 0 :
                currentLevelIndex < 50 ? 1 :
                currentLevelIndex < 150 ? 2 : 3;
            int hiddenCount = Mathf.Min(desiredHidden, Mathf.Max(0, level.WordCount - 1));
            if (hiddenCount == 0) return;

            List<int> candidates = Enumerable.Range(0, level.WordCount).ToList();
            System.Random random = new System.Random(level.Seed ^ 0x51A7);
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int swapIndex = random.Next(i + 1);
                (candidates[i], candidates[swapIndex]) = (candidates[swapIndex], candidates[i]);
            }

            for (int i = 0; i < hiddenCount; i++) hiddenWordIndices.Add(candidates[i]);
        }

        private void TryRevealWord(int wordIndex)
        {
            if (wordIndex < 0 || wordIndex >= targetChips.Count || !targetChips[wordIndex].IsHidden) return;
            if (!GameProgress.TrySpendCoins(WordRevealCost))
            {
                helperText.text = $"YOU NEED {WordRevealCost - GameProgress.Coins} MORE COINS";
                helperText.color = Danger;
                ShowShop();
                return;
            }

            GameProgress.RevealWord(currentLevelIndex, wordIndex);
            targetChips[wordIndex].Reveal();
            helperText.text = "WORD REVEALED!";
            helperText.color = Gold;
            RefreshCoinText();
        }

        private void CreateTube(int index, Vector2 position, Vector2 size, float tileSize,
            float slotSpacing, float railThickness, float bottomThickness, float bottomPadding, bool isBonus)
        {
            RectTransform root = CreateRect("Tube " + (index + 1), boardContent);
            SetRect(root, position, size);

            Image shadow = AddImage("Shadow", root, null, new Color32(8, 6, 31, 125));
            SetRect(shadow.rectTransform, new Vector2(0f, -9f), size + new Vector2(10f, 10f));
            shadow.raycastTarget = false;

            float cavityHeight = size.y - bottomThickness - 3f;
            Image cavity = AddImage("Cavity", root, null, new Color32(27, 20, 76, 235));
            SetRect(cavity.rectTransform, new Vector2(0f, (bottomThickness - 3f) * 0.5f),
                new Vector2(size.x - railThickness * 2f, cavityHeight));
            cavity.raycastTarget = false;

            Image glow = AddImage("Selection Glow", root, null, new Color32(72, 215, 255, 0));
            SetRect(glow.rectTransform, new Vector2(0f, (bottomThickness - 3f) * 0.5f),
                new Vector2(size.x - railThickness * 2f - 5f, cavityHeight - 5f));
            glow.raycastTarget = false;

            Color railColor = isBonus ? new Color32(181, 125, 24, 255) : new Color32(105, 55, 207, 255);
            Color railHighlight = isBonus ? Gold : new Color32(190, 117, 255, 255);
            float railX = size.x * 0.5f - railThickness * 0.5f;
            CreateTubePart("Left Rail", root, new Vector2(-railX, 0f),
                new Vector2(railThickness, size.y), railColor);
            CreateTubePart("Right Rail", root, new Vector2(railX, 0f),
                new Vector2(railThickness, size.y), railColor);
            CreateTubePart("Bottom Rail", root, new Vector2(0f, -size.y * 0.5f + bottomThickness * 0.5f),
                new Vector2(size.x, bottomThickness), railColor);

            float shineWidth = Mathf.Max(2.5f, railThickness * 0.24f);
            CreateTubePart("Left Highlight", root, new Vector2(-railX - railThickness * 0.18f, 2f),
                new Vector2(shineWidth, size.y - 8f), railHighlight);
            CreateTubePart("Right Highlight", root, new Vector2(railX - railThickness * 0.18f, 2f),
                new Vector2(shineWidth, size.y - 8f), railHighlight);
            CreateTubePart("Bottom Highlight", root,
                new Vector2(0f, -size.y * 0.5f + bottomThickness - shineWidth * 0.5f),
                new Vector2(size.x - railThickness, shineWidth), railHighlight);

            float lipWidth = railThickness * 1.65f;
            float lipHeight = Mathf.Max(6f, railThickness * 0.62f);
            CreateTubePart("Left Lip", root,
                new Vector2(-size.x * 0.5f + lipWidth * 0.5f, size.y * 0.5f - lipHeight * 0.5f),
                new Vector2(lipWidth, lipHeight), railHighlight);
            CreateTubePart("Right Lip", root,
                new Vector2(size.x * 0.5f - lipWidth * 0.5f, size.y * 0.5f - lipHeight * 0.5f),
                new Vector2(lipWidth, lipHeight), railHighlight);

            bool purchaseLocked = isBonus && !GameProgress.IsBonusTubeUnlocked(currentLevelIndex);
            Image lockPanel = AddImage("Bonus Lock", root, null, new Color32(16, 13, 43, 238));
            SetRect(lockPanel.rectTransform, new Vector2(0f, (bottomThickness - 3f) * 0.5f),
                new Vector2(size.x - railThickness * 2f - 3f, cavityHeight - 3f));
            lockPanel.raycastTarget = false;
            TextMeshProUGUI lockLabel = AddText("Lock Label", lockPanel.rectTransform,
                $"LOCKED\n{BonusTubeCost} COINS", Mathf.Clamp(tileSize * 0.25f, 18f, 28f), FontStyles.Bold);
            Stretch(lockLabel.rectTransform, new Vector2(4, 8), new Vector2(-4, -8));
            lockLabel.color = Gold;
            lockLabel.enableAutoSizing = true;
            lockLabel.fontSizeMin = 13;
            lockLabel.fontSizeMax = 28;
            lockLabel.raycastTarget = false;
            lockPanel.gameObject.SetActive(purchaseLocked);

            Image hitArea = AddImage("Hit Area", root, null, new Color32(255, 255, 255, 1));
            Stretch(hitArea.rectTransform);
            hitArea.raycastTarget = true;
            Button button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = hitArea;
            button.transition = Selectable.Transition.None;
            float firstSlotY = position.y - size.y * 0.5f + bottomThickness + bottomPadding + tileSize * 0.5f;
            Tube tube = new Tube(index, root, glow, position, size.y, firstSlotY, tileSize, slotSpacing,
                isBonus, purchaseLocked, lockPanel.gameObject);
            tubes.Add(tube);
            if (isBonus) bonusTube = tube;
            button.onClick.AddListener(PlayClick);
            button.onClick.AddListener(() => OnTubePressed(tube));
        }

        private static Image CreateTubePart(string name, Transform parent, Vector2 position, Vector2 size, Color color)
        {
            Image part = AddImage(name, parent, null, color);
            SetRect(part.rectTransform, position, size);
            part.raycastTarget = false;
            return part;
        }

        private void OnTubePressed(Tube tube)
        {
            if (isAnimating || hasWon) return;
            if (tube.PurchaseLocked)
            {
                TryUnlockBonusTube(tube);
                return;
            }

            if (selectedTube == null)
            {
                SelectTube(tube);
                return;
            }

            if (selectedTube == tube)
            {
                DeselectTube();
                return;
            }

            if (tube.WordLocked || tube.Tiles.Count >= level.WordLength)
            {
                InvalidMove(tube);
                return;
            }

            MoveSelectedTile(tube);
        }

        private void SelectTube(Tube tube)
        {
            if (tube.WordLocked || tube.PurchaseLocked || tube.Tiles.Count == 0)
            {
                InvalidMove(tube);
                return;
            }

            selectedTube = tube;
            tube.SetHighlight(Cyan, 0.62f);
            LetterTile tile = tube.Top;
            tile.Rect.DOKill();
            tile.Rect.SetAsLastSibling();
            tile.Rect.DOAnchorPos(tube.LiftPosition, LiftDuration).SetEase(Ease.OutBack).SetTarget(gameRoot);
            tile.Rect.DOScale(1.08f, LiftDuration).SetEase(Ease.OutBack).SetTarget(gameRoot);
            helperText.text = "NOW SELECT A DESTINATION TUBE";
            helperText.color = Cyan;
        }

        private void DeselectTube()
        {
            if (selectedTube == null) return;
            Tube tube = selectedTube;
            LetterTile tile = tube.Top;
            tube.ClearHighlight();
            tile.Rect.DOKill();
            tile.Rect.DOAnchorPos(tube.SlotPosition(tube.Tiles.Count - 1), LiftDuration).SetEase(Ease.OutQuad)
                .SetTarget(gameRoot);
            tile.Rect.DOScale(1f, LiftDuration).SetTarget(gameRoot);
            selectedTube = null;
            SetDefaultHelper();
        }

        private void MoveSelectedTile(Tube destination)
        {
            Tube source = selectedTube;
            LetterTile tile = source.Top;
            selectedTube = null;
            source.ClearHighlight();
            TransferTile(source, destination, tile, true, true, () => EvaluateCompletedTubes(true));
        }

        private void TransferTile(Tube source, Tube destination, LetterTile tile, bool recordUndo, bool countMove,
            Action onComplete)
        {
            source.Tiles.RemoveAt(source.Tiles.Count - 1);
            destination.Tiles.Add(tile);
            isAnimating = true;
            if (recordUndo) undoStack.Push(new MoveRecord(source, destination, tile));
            if (countMove) moves++;
            UpdateMoveText();
            Vector2 target = destination.SlotPosition(destination.Tiles.Count - 1);
            float bridgeY = Mathf.Max(source.LiftPosition.y, destination.LiftPosition.y) + 42f;
            tile.Rect.SetAsLastSibling();
            Sequence sequence = DOTween.Sequence().SetTarget(gameRoot);
            sequence.Append(tile.Rect.DOAnchorPosY(bridgeY, LiftDuration).SetEase(Ease.OutQuad));
            sequence.Append(tile.Rect.DOAnchorPosX(target.x, TravelDuration).SetEase(Ease.InOutSine));
            sequence.Join(tile.Rect
                .DORotate(new Vector3(0, 0, source.Index < destination.Index ? -7f : 7f), TravelDuration * 0.52f)
                .SetLoops(2, LoopType.Yoyo).SetEase(Ease.InOutSine));
            sequence.Append(tile.Rect.DOAnchorPos(target, DropDuration).SetEase(Ease.OutBounce));
            sequence.Join(tile.Rect.DOScale(1f, DropDuration).SetEase(Ease.OutBack));
            sequence.OnComplete(() =>
            {
                tile.Rect.localRotation = Quaternion.identity;
                isAnimating = false;
                SetDefaultHelper();
                onComplete?.Invoke();
            });
        }

        private void UndoLastMove()
        {
            if (isAnimating || hasWon) return;
            if (selectedTube != null) DeselectTube();
            if (undoStack.Count == 0)
            {
                helperText.text = "NOTHING TO UNDO";
                helperText.color = Danger;
                return;
            }

            if (freeUndosRemaining <= 0 && GameProgress.UndoTokens <= 0)
            {
                helperText.text = "NO UNDOS LEFT";
                helperText.color = Danger;
                ShowShop();
                return;
            }

            MoveRecord record = undoStack.Peek();
            if (record.Destination.Top != record.Tile)
            {
                undoStack.Clear();
                helperText.text = "UNDO HISTORY CLEARED";
                helperText.color = Danger;
                return;
            }

            undoStack.Pop();
            if (freeUndosRemaining > 0) freeUndosRemaining--;
            else GameProgress.TrySpendUndoToken();
            RefreshUndoText();

            record.Destination.WordLocked = false;
            moves = Mathf.Max(0, moves - 1);
            TransferTile(record.Destination, record.Source, record.Tile, false, false,
                () => EvaluateCompletedTubes(false));
        }

        private void ResetLevel()
        {
            if (isAnimating) return;
            if (selectedTube != null) DeselectTube();
            boardContent.DOPunchScale(Vector3.one * 0.018f, 0.25f, 5, 0.5f).SetTarget(gameRoot)
                .OnComplete(() => BuildCurrentLevelBoard(CloneStacks(initialStacks)));
        }

        private void EvaluateCompletedTubes(bool animate)
        {
            bool[] usedWords = new bool[level.WordCount];
            for (int i = 0; i < targetChips.Count; i++) targetChips[i].SetCompleted(false, false);
            int completed = 0;
            foreach (Tube tube in tubes)
            {
                bool wasLocked = tube.WordLocked;
                tube.WordLocked = false;
                if (tube.Tiles.Count != level.WordLength)
                {
                    if (wasLocked) tube.ClearHighlight();
                    continue;
                }

                string value = new string(tube.Tiles.Select(tile => tile.Letter).ToArray());
                for (int wordIndex = 0; wordIndex < level.WordCount; wordIndex++)
                {
                    if (usedWords[wordIndex]) continue;
                    string word = level.Words[wordIndex];
                    if (value != word && value != Reverse(word)) continue;
                    usedWords[wordIndex] = true;
                    tube.WordLocked = true;
                    targetChips[wordIndex].SetCompleted(true, animate && !wasLocked);
                    completed++;
                    if (animate && !wasLocked)
                    {
                        tube.PlayCompleted();
                        for (int tileIndex = 0; tileIndex < tube.Tiles.Count; tileIndex++)
                            tube.Tiles[tileIndex].Rect.DOPunchScale(Vector3.one * 0.12f, 0.45f, 5, 0.55f)
                                .SetDelay(tileIndex * 0.055f).SetTarget(gameRoot);
                    }

                    break;
                }
            }

            if (completed == level.WordCount && !hasWon)
            {
                hasWon = true;
                bool firstCompletion = !GameProgress.IsCompleted(currentLevelIndex);
                GameProgress.CompleteLevel(currentLevelIndex, moves);
                completionReward = firstCompletion ? FirstCompletionReward : 0;
                if (completionReward > 0) GameProgress.AddCoins(completionReward);
                RefreshCoinText();
                PlaySuccess();
                VibrateIfEnabled();
                DOVirtual.DelayedCall(0.7f, ShowWinPanel).SetTarget(gameRoot);
            }
        }

        private void ShowWinPanel()
        {
            winTitleText.text = currentLevelIndex == WordLevelCatalog.TotalLevels - 1
                ? "YOU COMPLETED THE GAME!"
                : "LEVEL COMPLETE!";
            int best = GameProgress.BestMoves(currentLevelIndex);
            winStatsText.text = completionReward > 0
                ? $"MOVES: {moves}\nBEST: {best}\n+{completionReward} COINS"
                : $"MOVES: {moves}\nBEST: {best}";
            nextButtonText.text = currentLevelIndex == WordLevelCatalog.TotalLevels - 1 ? "MAIN MENU" : "NEXT LEVEL";
            ShowPanel(winPanel);
        }

        private void NextLevel()
        {
            HidePanelInstant(winPanel);
            void Continue()
            {
                if (currentLevelIndex >= WordLevelCatalog.TotalLevels - 1) ShowMainMenu();
                else LoadLevel(currentLevelIndex + 1);
            }

            if (monetization == null) Continue();
            else monetization.ShowInterstitialIfDue(Continue);
        }

        private void InvalidMove(Tube tube)
        {
            tube.Rect.DOKill();
            tube.Rect.DOShakeAnchorPos(0.3f, new Vector2(15f, 0f), 12, 90f, false, true).SetTarget(gameRoot);
            helperText.text = tube.PurchaseLocked ? $"UNLOCK FOR {BonusTubeCost} COINS" :
                tube.WordLocked ? "THIS WORD IS COMPLETE" :
                tube.Tiles.Count >= level.WordLength ? "THIS TUBE IS FULL" : "THIS TUBE IS EMPTY";
            helperText.color = Danger;
            VibrateIfEnabled();
        }

        private void TryUnlockBonusTube(Tube tube)
        {
            if (!tube.IsBonus || !tube.PurchaseLocked) return;
            if (selectedTube != null) DeselectTube();
            if (!GameProgress.TrySpendCoins(BonusTubeCost))
            {
                helperText.text = $"YOU NEED {BonusTubeCost - GameProgress.Coins} MORE COINS";
                helperText.color = Danger;
                ShowShop();
                return;
            }

            GameProgress.UnlockBonusTube(currentLevelIndex);
            tube.UnlockPurchase();
            tube.SetHighlight(Gold, 0.7f);
            tube.Rect.DOPunchScale(Vector3.one * 0.075f, 0.55f, 6, 0.55f).SetTarget(gameRoot);
            helperText.text = "BONUS TUBE UNLOCKED!";
            helperText.color = Gold;
            RefreshCoinText();
        }

        private static void VibrateIfEnabled()
        {
#if !UNITY_EDITOR
            if (GameProgress.Vibration) Handheld.Vibrate();
#endif
        }

        private void SetDefaultHelper()
        {
            helperText.text = "SELECT A TOP LETTER";
            helperText.color = Muted;
        }

        private void UpdateMoveText() => moveText.text = "MOVES: " + moves;

        private List<List<char>> GenerateScrambledStacks(WordLevelDefinition definition)
        {
            System.Random random = new System.Random(definition.Seed);
            List<char> letters = definition.Words.SelectMany(word => word).ToList();
            List<List<char>> stacks = null;

            // Keep the two standard helpers and the purchasable bonus tube empty.
            // The bonus starts locked, while the standard buffers preserve the
            // guarantee that every generated puzzle remains solvable for free.
            for (int attempt = 0; attempt < 64; attempt++)
            {
                List<char> shuffled = new List<char>(letters);
                for (int i = shuffled.Count - 1; i > 0; i--)
                {
                    int swapIndex = random.Next(i + 1);
                    (shuffled[i], shuffled[swapIndex]) = (shuffled[swapIndex], shuffled[i]);
                }

                stacks = new List<List<char>>(definition.TubeCount + 1);
                for (int tube = 0; tube < definition.WordCount; tube++)
                    stacks.Add(shuffled.GetRange(tube * definition.WordLength, definition.WordLength));
                stacks.Add(new List<char>());
                stacks.Add(new List<char>());
                stacks.Add(new List<char>());
                if (!HasCompletedStack(stacks, definition.Words)) return stacks;
            }

            return stacks;
        }

        private static bool HasCompletedStack(IEnumerable<List<char>> stacks, IReadOnlyList<string> words) =>
            stacks.Any(stack => IsCompleted(stack, words));

        private static bool IsCompleted(List<char> stack, IReadOnlyList<string> words)
        {
            if (words.Count == 0 || stack.Count != words[0].Length) return false;
            string value = new string(stack.ToArray());
            return words.Any(word => value == word || value == Reverse(word));
        }

        private static List<List<char>> CloneStacks(IEnumerable<List<char>> stacks) =>
            stacks.Select(stack => new List<char>(stack)).ToList();

        private void RefreshLevelButtons()
        {
            int highest = GameProgress.HighestUnlocked;
            foreach (LevelButtonView view in levelButtons)
            {
                bool unlocked = enableLevelTesting || view.LevelIndex <= highest;
                bool complete = GameProgress.IsCompleted(view.LevelIndex);
                view.Button.interactable = unlocked;
                view.Image.color = complete ? Green : unlocked ? Purple : new Color32(55, 49, 80, 255);
                view.Label.text = complete ? $"{view.LevelIndex + 1}\nDONE" :
                    unlocked ? (view.LevelIndex + 1).ToString() : "LOCKED";
                view.Label.color = unlocked ? Color.white : new Color32(130, 125, 153, 255);
            }
        }

        private void ShowSettings()
        {
            RefreshSettingsText();
            ShowPanel(settingsPanel);
        }

        private void HideSettings() => HidePanel(settingsPanel);

        private void ShowShop()
        {
            RefreshCoinText();
            RefreshUndoText();
            RefreshShopPrices();
            if (shopStatusText != null) shopStatusText.text = string.Empty;
            ShowPanel(shopPanel);
        }

        private void HideShop() => HidePanel(shopPanel);

        private void RefreshShopPrices()
        {
            if (monetization == null) return;
            if (smallPackPriceText != null)
                smallPackPriceText.text = monetization.GetLocalizedPrice(monetization.Config.smallCoinsProductId, "BUY");
            if (mediumPackPriceText != null)
                mediumPackPriceText.text = monetization.GetLocalizedPrice(monetization.Config.mediumCoinsProductId, "BUY");
            if (largePackPriceText != null)
                largePackPriceText.text = monetization.GetLocalizedPrice(monetization.Config.largeCoinsProductId, "BUY");
            if (removeAdsPriceText != null)
                removeAdsPriceText.text = monetization.AdsRemoved
                    ? "OWNED"
                    : monetization.GetLocalizedPrice(monetization.Config.removeAdsProductId, "BUY");
        }

        private void OnMonetizationStatus(string message)
        {
            if (shopStatusText == null) return;
            shopStatusText.text = message;
            shopStatusText.color = message.Contains("FAILED") || message.Contains("NOT ") ? Danger : Green;
        }

        private void OnMonetizationBalancesChanged()
        {
            RefreshCoinText();
            RefreshUndoText();
            RefreshShopPrices();
            if (shopPanel != null && shopPanel.gameObject.activeSelf)
                shopPanel.transform.DOPunchScale(Vector3.one * 0.025f, 0.3f, 4, 0.5f);
        }

        private void BuyUndoPack()
        {
            if (!GameProgress.TrySpendCoins(UndoPackCost))
            {
                shopStatusText.text = $"YOU NEED {UndoPackCost - GameProgress.Coins} MORE COINS";
                shopStatusText.color = Danger;
                shopStatusText.rectTransform.DOShakeAnchorPos(0.3f, new Vector2(12f, 0f), 10, 90f, false, true);
                return;
            }

            GameProgress.AddUndoTokens(UndoPackAmount);
            shopStatusText.text = $"+{UndoPackAmount} UNDO ADDED!";
            shopStatusText.color = Green;
            RefreshCoinText();
            RefreshUndoText();
            shopPanel.transform.DOPunchScale(Vector3.one * 0.025f, 0.3f, 4, 0.5f);
        }

        private void ToggleSound()
        {
            GameProgress.Sound = !GameProgress.Sound;
            RefreshSettingsText();
        }

        private void ToggleMusic()
        {
            GameProgress.Music = !GameProgress.Music;
            ApplyAudioSettings();
            RefreshSettingsText();
        }

        private void ToggleVibration()
        {
            GameProgress.Vibration = !GameProgress.Vibration;
            RefreshSettingsText();
        }

        private void RefreshSettingsText()
        {
            soundValueText.text = GameProgress.Sound ? "ON" : "OFF";
            musicValueText.text = GameProgress.Music ? "ON" : "OFF";
            vibrationValueText.text = GameProgress.Vibration ? "ON" : "OFF";
        }

        private void RefreshCoinText()
        {
            foreach (TextMeshProUGUI text in coinTexts)
                if (text != null)
                    text.text = $"COINS  {GameProgress.Coins}";
        }

        private void RefreshUndoText()
        {
            int total = freeUndosRemaining + GameProgress.UndoTokens;
            if (undoButtonText != null) undoButtonText.text = total.ToString();
            if (shopUndoText != null) shopUndoText.text = $"EXTRA UNDO  {GameProgress.UndoTokens}";
        }

        private void BeginStartupLoading()
        {
            if (startupRoutine != null) return;
            startupRoutine = StartCoroutine(InitializeGameRoutine());
        }

        private IEnumerator InitializeGameRoutine()
        {
            const float minimumVisibleTime = 2.5f;
            float loadingShownAt = Time.realtimeSinceStartup;
            BeginLoadingVisual("BUILDING GAME INTERFACE", 0.12f);
            loadingPanel.DOFade(1f, 0.16f).SetTarget(loadingPanel);
            yield return null;

            SetLoadingStatus("CREATING BACKGROUND MUSIC", 0.25f);
            if (musicAudio.clip == null) musicAudio.clip = CreateBackgroundMusic();
            ApplyAudioSettings();
            yield return null;

            SetLoadingStatus("LOADING LEVEL CATALOG", 0.34f);
            _ = WordLevelCatalog.TotalLevels;
            yield return null;

            monetization.InitializeServices();
            float startedAt = Time.realtimeSinceStartup;
            const float maximumWait = 12f;
            while (!monetization.InitializationComplete && Time.realtimeSinceStartup - startedAt < maximumWait)
                yield return null;

            if (!monetization.InitializationComplete)
            {
                SetLoadingStatus("ONLINE SERVICES WILL CONTINUE IN BACKGROUND", 0.92f);
                yield return new WaitForSecondsRealtime(0.45f);
            }

            while (Time.realtimeSinceStartup - loadingShownAt < minimumVisibleTime)
                yield return null;

            SetLoadingStatus("READY!", 1f);
            yield return new WaitForSecondsRealtime(0.3f);
            bool finished = false;
            FinishLoadingVisual(() => finished = true);
            while (!finished) yield return null;
            startupRoutine = null;
        }

        private void OnInitializationProgress(string message, float progress)
        {
            SetLoadingStatus(message, progress);
        }

        private void SetLoadingStatus(string message, float progress)
        {
            if (loadingStatusText != null) loadingStatusText.text = message;
            if (loadingProgressFill != null)
            {
                Vector2 maximum = loadingProgressFill.rectTransform.anchorMax;
                maximum.x = Mathf.Clamp(progress, 0.02f, 1f);
                loadingProgressFill.rectTransform.anchorMax = maximum;
            }
        }

        private void BeginLoadingVisual(string status, float progress)
        {
            if (loadingPanel == null) return;
            isLoading = true;
            SetLoadingStatus(status, progress);
            loadingPanel.gameObject.SetActive(true);
            loadingPanel.transform.SetAsLastSibling();
            loadingPanel.alpha = 0f;
            loadingPanel.interactable = true;
            loadingPanel.blocksRaycasts = true;
            DOTween.Kill(loadingPanel, false);
            loadingSpinner.localRotation = Quaternion.identity;
            loadingSpinner.DORotate(new Vector3(0, 0, -360f), 0.85f, RotateMode.FastBeyond360)
                .SetEase(Ease.Linear).SetLoops(-1).SetTarget(loadingPanel);
            for (int i = 0; i < loadingTiles.Count; i++)
            {
                RectTransform tile = loadingTiles[i];
                tile.localScale = Vector3.one;
                tile.anchoredPosition = new Vector2(tile.anchoredPosition.x, -215f);
                tile.DOAnchorPosY(-185f, 0.32f).SetDelay(i * 0.09f).SetEase(Ease.OutQuad)
                    .SetLoops(-1, LoopType.Yoyo).SetTarget(loadingPanel);
                tile.DOScale(1.1f, 0.32f).SetDelay(i * 0.09f).SetEase(Ease.InOutSine)
                    .SetLoops(-1, LoopType.Yoyo).SetTarget(loadingPanel);
            }
        }

        private void FinishLoadingVisual(Action onComplete)
        {
            loadingPanel.DOFade(0f, 0.16f).SetTarget(loadingPanel).OnComplete(() =>
            {
                DOTween.Kill(loadingPanel, false);
                loadingPanel.interactable = false;
                loadingPanel.blocksRaycasts = false;
                loadingPanel.gameObject.SetActive(false);
                isLoading = false;
                onComplete?.Invoke();
            });
        }

        private void ShowLoading(Action onReady, float duration)
        {
            if (loadingPanel == null) return;
            BeginLoadingVisual("LOADING LEVEL", 0.35f);

            Sequence sequence = DOTween.Sequence().SetTarget(loadingPanel);
            sequence.Append(loadingPanel.DOFade(1f, 0.16f));
            sequence.AppendInterval(Mathf.Max(0.2f, duration - 0.32f));
            sequence.AppendCallback(() => onReady?.Invoke());
            sequence.AppendCallback(() => SetLoadingStatus("READY!", 1f));
            sequence.OnComplete(() => FinishLoadingVisual(null));
        }

        private void ApplyAudioSettings()
        {
            if (musicAudio == null) return;
            if (GameProgress.Music && !musicAudio.isPlaying) musicAudio.Play();
            else if (!GameProgress.Music && musicAudio.isPlaying) musicAudio.Pause();
        }

        private void PlayClick()
        {
            if (GameProgress.Sound && uiAudio != null && clickClip != null) uiAudio.PlayOneShot(clickClip);
        }

        private void PlaySuccess()
        {
            if (GameProgress.Sound && uiAudio != null && successClip != null) uiAudio.PlayOneShot(successClip, 0.8f);
        }

        private LetterTile CreateLetterTile(char letter, int visualIndex, float size)
        {
            Sprite sprite = GetLetterSprite(letter, visualIndex);
            Image image = AddImage("Letter " + letter, piecesLayer, sprite, Color.white);
            SetRect(image.rectTransform, Vector2.zero, new Vector2(size, size * 0.96f));
            image.preserveAspect = true;
            image.raycastTarget = false;
            if (sprite == null) image.color = Color.HSVToRGB((visualIndex * 0.17f) % 1f, 0.7f, 1f);
            TextMeshProUGUI label = AddText("Label", image.rectTransform, letter.ToString(), size * 0.52f,
                FontStyles.Bold);
            Stretch(label.rectTransform, new Vector2(10, 8), new Vector2(-10, -6));
            label.raycastTarget = false;
            label.outlineColor = new Color32(34, 23, 67, 175);
            label.outlineWidth = 0.15f;
            label.enableAutoSizing = true;
            label.fontSizeMin = 30;
            label.fontSizeMax = size * 0.52f;
            return new LetterTile(letter, image.rectTransform);
        }

        private Sprite GetLetterSprite(char letter, int visualIndex)
        {
            if (letterSprites == null || letterSprites.Length == 0) return null;
            return letterSprites[Mathf.Abs(letter + visualIndex * 3) % letterSprites.Length];
        }

        private TargetChip CreateTargetChip(string word, int wordIndex, bool hidden, int revealCost,
            Vector2 position, Vector2 size, Color color)
        {
            Image image = AddImage("Target " + word, targetContainer, roundedButtonSprite, color);
            image.type = Image.Type.Sliced;
            SetRect(image.rectTransform, position, size);
            image.raycastTarget = true;
            TextMeshProUGUI label = AddText("Text", image.rectTransform, word,
                Mathf.Min(39f, size.x / (word.Length * 0.72f)), FontStyles.Bold);
            Stretch(label.rectTransform, new Vector2(8, 4), new Vector2(-8, -4));
            label.enableAutoSizing = true;
            label.fontSizeMin = 22;
            label.fontSizeMax = 39;
            label.raycastTarget = false;
            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(PlayClick);
            button.onClick.AddListener(() => TryRevealWord(wordIndex));
            return new TargetChip(image, label, color, word, hidden, revealCost, button);
        }

        private TextMeshProUGUI CreateSettingRow(Transform parent, string title, float y,
            UnityEngine.Events.UnityAction action)
        {
            Image row = AddImage(title, parent, null, BackgroundLight);
            SetRect(row.rectTransform, new Vector2(0, y), new Vector2(690, 105));
            TextMeshProUGUI label = AddText("Label", row.rectTransform, title,
                title.Length > 12 ? 28 : 34, FontStyles.Bold);
            SetRect(label.rectTransform, new Vector2(-165, 0), new Vector2(330, 70));
            label.alignment = TextAlignmentOptions.MidlineLeft;
            Button toggle = CreateButton(row.rectTransform, "ON", new Vector2(235, 0), new Vector2(170, 72), Purple,
                30);
            toggle.onClick.AddListener(action);
            return toggle.GetComponentInChildren<TextMeshProUGUI>();
        }

        private Button CreateLayoutButton(Transform parent, int number)
        {
            GameObject go = new GameObject("Level " + number, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Button));
            go.layer = 5;
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.sprite = roundedButtonSprite;
            image.type = Image.Type.Sliced;
            image.color = Purple;
            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(PlayClick);
            TextMeshProUGUI label = AddText("Text", rect, number.ToString(), 32, FontStyles.Bold);
            Stretch(label.rectTransform, new Vector2(5, 5), new Vector2(-5, -5));
            label.raycastTarget = false;
            return button;
        }

        private Button CreateButton(Transform parent, string label, Vector2 position, Vector2 size, Color color,
            float fontSize)
        {
            Image image = AddImage(label + " Button", parent, roundedButtonSprite, color);
            image.type = Image.Type.Sliced;
            SetRect(image.rectTransform, position, size);
            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.13f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.17f);
            colors.disabledColor = new Color32(65, 59, 88, 190);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.onClick.AddListener(PlayClick);
            TextMeshProUGUI text = AddText("Text", image.rectTransform, label, fontSize, FontStyles.Bold);
            Sprite iconSprite = GetControlIcon(label);
            if (iconSprite != null)
            {
                bool showUndoCount = label == "UNDO";
                float iconSize = Mathf.Clamp(size.y * 0.62f, 34f, 64f);
                Image icon = AddImage("Icon", image.rectTransform, iconSprite, Color.white);
                bool compactSquare = showUndoCount && Mathf.Abs(size.x - size.y) < 2f;
                SetRect(icon.rectTransform,
                    compactSquare ? new Vector2(0f, 13f) : new Vector2(showUndoCount ? -42f : 0f, 1f),
                    new Vector2(iconSize, iconSize));
                icon.preserveAspect = true;
                icon.raycastTarget = false;

                if (showUndoCount)
                {
                    SetRect(text.rectTransform, compactSquare ? new Vector2(0f, -37f) : new Vector2(50f, 0f),
                        compactSquare ? new Vector2(80f, 28f) : new Vector2(90f, size.y - 12f));
                    text.fontSize = compactSquare ? 21f : Mathf.Min(fontSize, 30f);
                }
                else
                {
                    text.gameObject.SetActive(false);
                }
            }
            else
            {
                Stretch(text.rectTransform, new Vector2(14, 8), new Vector2(-14, -8));
            }

            text.raycastTarget = false;
            return button;
        }

        private Sprite GetControlIcon(string label)
        {
            if (label == "SET") return settingsIcon;
            if (label == "‹") return backIcon;
            if (label == "UNDO") return undoIcon;
            if (label == "RESET") return resetIcon;
            return null;
        }

        private void CreateCoinBadge(Transform parent, Vector2 position, bool opensShop)
        {
            Image badge = AddImage("Coin Balance", parent, roundedButtonSprite, new Color32(173, 112, 21, 255));
            badge.type = Image.Type.Sliced;
            SetRect(badge.rectTransform, position, new Vector2(235, 78));
            TextMeshProUGUI text = AddText("Text", badge.rectTransform, string.Empty, 27, FontStyles.Bold);
            Stretch(text.rectTransform, new Vector2(10, 5), new Vector2(-10, -5));
            text.color = Color.white;
            text.raycastTarget = false;
            coinTexts.Add(text);
            if (opensShop)
            {
                Button button = badge.gameObject.AddComponent<Button>();
                button.targetGraphic = badge;
                button.onClick.AddListener(PlayClick);
                button.onClick.AddListener(ShowShop);
            }
        }

        private void AddDecoration(Vector2 position, Vector2 size, Color color)
        {
            Image image = AddImage("Soft Glow", gameRoot, null, color);
            SetRect(image.rectTransform, position, size);
            image.raycastTarget = false;
            image.transform.SetAsFirstSibling();
        }

        private static void ShowPanel(CanvasGroup panel)
        {
            panel.gameObject.SetActive(true);
            panel.alpha = 0f;
            panel.transform.localScale = Vector3.one * 0.82f;
            panel.DOFade(1f, 0.2f);
            panel.transform.DOScale(1f, 0.32f).SetEase(Ease.OutBack);
            panel.interactable = true;
            panel.blocksRaycasts = true;
        }

        private static void HidePanel(CanvasGroup panel)
        {
            if (panel == null || !panel.gameObject.activeSelf) return;
            panel.interactable = false;
            panel.blocksRaycasts = false;
            panel.DOFade(0f, 0.15f).OnComplete(() => panel.gameObject.SetActive(false));
        }

        private static void HidePanelInstant(CanvasGroup panel)
        {
            if (panel == null) return;
            panel.DOKill();
            panel.alpha = 0f;
            panel.interactable = false;
            panel.blocksRaycasts = false;
            panel.gameObject.SetActive(false);
        }

        private void StopGameplayTweens()
        {
            if (gameRoot != null) DOTween.Kill(gameRoot, false);
            isAnimating = false;
            selectedTube = null;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static Image AddImage(string name, Transform parent, Sprite sprite, Color color)
        {
            RectTransform rect = CreateRect(name, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            return image;
        }

        private static TextMeshProUGUI AddText(string name, Transform parent, string value, float size,
            FontStyles style)
        {
            RectTransform rect = CreateRect(name, parent);
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return text;
        }

        private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
        }

        private static void Stretch(RectTransform rect) => Stretch(rect, Vector2.zero, Vector2.zero);

        private static void Stretch(RectTransform rect, Vector2 minOffset, Vector2 maxOffset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = minOffset;
            rect.offsetMax = maxOffset;
            rect.localScale = Vector3.one;
        }

        private static string Reverse(string value)
        {
            char[] chars = value.ToCharArray();
            Array.Reverse(chars);
            return new string(chars);
        }

        private static AudioClip CreateTone(string name, float frequency, float duration, float amplitude)
        {
            const int sampleRate = 22050;
            int count = Mathf.CeilToInt(duration * sampleRate);
            float[] samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                float fade = 1f - i / (float)count;
                samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * i / sampleRate) * amplitude * fade;
            }

            AudioClip clip = AudioClip.Create(name, count, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioClip CreateSuccessTone()
        {
            const int sampleRate = 22050;
            int count = Mathf.CeilToInt(0.55f * sampleRate);
            float[] samples = new float[count];
            float[] notes = { 523.25f, 659.25f, 783.99f };
            for (int i = 0; i < count; i++)
            {
                int note = Mathf.Min(2, Mathf.FloorToInt(i / (float)count * 3));
                float fade = 1f - i / (float)count;
                samples[i] = Mathf.Sin(2f * Mathf.PI * notes[note] * i / sampleRate) * 0.2f * fade;
            }

            AudioClip clip = AudioClip.Create("Success", count, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioClip CreateBackgroundMusic()
        {
            const int sampleRate = 22050;
            const int seconds = 16;
            const int channels = 2;
            int frameCount = sampleRate * seconds;
            float[] samples = new float[frameCount * channels];
            float[][] chords =
            {
                new[] { 130.81f, 164.81f, 196.00f },
                new[] { 110.00f, 130.81f, 164.81f },
                new[] { 87.31f, 110.00f, 130.81f },
                new[] { 98.00f, 123.47f, 146.83f }
            };
            float[] melody = { 523.25f, 659.25f, 783.99f, 659.25f, 440.00f, 523.25f, 698.46f, 587.33f };

            for (int frame = 0; frame < frameCount; frame++)
            {
                float time = frame / (float)sampleRate;
                int chordIndex = Mathf.Min(chords.Length - 1, Mathf.FloorToInt(time / 4f));
                float edgeFade = Mathf.SmoothStep(0f, 1f,
                    Mathf.Min(time / 0.65f, (seconds - time) / 0.65f));
                float slowPulse = 0.72f + 0.28f * Mathf.Sin(2f * Mathf.PI * time / 8f);

                float leftPad = 0f;
                float rightPad = 0f;
                for (int note = 0; note < chords[chordIndex].Length; note++)
                {
                    float frequency = chords[chordIndex][note];
                    float phaseOffset = note * 0.37f;
                    leftPad += Mathf.Sin(2f * Mathf.PI * frequency * time + phaseOffset);
                    rightPad += Mathf.Sin(2f * Mathf.PI * frequency * time - phaseOffset);
                    leftPad += 0.22f * Mathf.Sin(2f * Mathf.PI * frequency * 2f * time + phaseOffset);
                    rightPad += 0.22f * Mathf.Sin(2f * Mathf.PI * frequency * 2f * time - phaseOffset);
                }

                int melodyIndex = Mathf.FloorToInt(time / 2f) % melody.Length;
                float noteTime = time % 2f;
                float bellEnvelope = Mathf.Exp(-2.8f * noteTime);
                float bell = (Mathf.Sin(2f * Mathf.PI * melody[melodyIndex] * time) +
                              0.28f * Mathf.Sin(2f * Mathf.PI * melody[melodyIndex] * 2.01f * time)) *
                             bellEnvelope;
                float shimmer = Mathf.Sin(2f * Mathf.PI * 1046.5f * time) *
                                (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * time / 4f)) * 0.035f;

                float left = (leftPad * 0.027f * slowPulse + bell * 0.055f + shimmer) * edgeFade;
                float right = (rightPad * 0.027f * slowPulse + bell * 0.052f - shimmer) * edgeFade;
                samples[frame * channels] = Mathf.Clamp(left, -0.22f, 0.22f);
                samples[frame * channels + 1] = Mathf.Clamp(right, -0.22f, 0.22f);
            }

            AudioClip clip = AudioClip.Create("Word Sort Dreamscape", frameCount, channels, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private sealed class LetterTile
        {
            public char Letter { get; }
            public RectTransform Rect { get; }

            public LetterTile(char letter, RectTransform rect)
            {
                Letter = letter;
                Rect = rect;
            }
        }

        private sealed class Tube
        {
            public int Index { get; }
            public RectTransform Rect { get; }
            public Image Glow { get; }
            public List<LetterTile> Tiles { get; } = new List<LetterTile>();
            public bool IsBonus { get; }
            public bool PurchaseLocked { get; private set; }
            public bool WordLocked { get; set; }
            public LetterTile Top => Tiles.Count == 0 ? null : Tiles[Tiles.Count - 1];
            public Vector2 LiftPosition => new Vector2(center.x, center.y + height * 0.5f + tileSize * 0.58f);
            private readonly Vector2 center;
            private readonly float height, firstSlotY, tileSize, slotSpacing;
            private readonly GameObject lockVisual;

            public Tube(int index, RectTransform rect, Image glow, Vector2 center, float height,
                float firstSlotY, float tileSize, float slotSpacing, bool isBonus, bool purchaseLocked,
                GameObject lockVisual)
            {
                Index = index;
                Rect = rect;
                Glow = glow;
                this.center = center;
                this.height = height;
                this.firstSlotY = firstSlotY;
                this.tileSize = tileSize;
                this.slotSpacing = slotSpacing;
                IsBonus = isBonus;
                PurchaseLocked = purchaseLocked;
                this.lockVisual = lockVisual;
            }

            public void UnlockPurchase()
            {
                PurchaseLocked = false;
                if (lockVisual != null) lockVisual.SetActive(false);
            }

            public Vector2 SlotPosition(int index)
            {
                return new Vector2(center.x, firstSlotY + Mathf.Max(0, index) * slotSpacing);
            }

            public void SetHighlight(Color color, float alpha)
            {
                Glow.DOKill();
                Color tinted = color;
                tinted.a = Glow.color.a;
                Glow.color = tinted;
                Glow.DOFade(alpha, 0.16f);
                Rect.DOScale(1.025f, 0.16f).SetEase(Ease.OutQuad);
            }

            public void ClearHighlight()
            {
                Glow.DOKill();
                Glow.DOFade(0f, 0.16f);
                Rect.DOScale(1f, 0.16f).SetEase(Ease.OutQuad);
            }

            public void PlayCompleted()
            {
                SetHighlight(Gold, 0.62f);
                Rect.DOPunchScale(Vector3.one * 0.045f, 0.55f, 5, 0.5f);
            }
        }

        private sealed class TargetChip
        {
            private readonly Image image;
            private readonly TextMeshProUGUI label;
            private readonly Color normalColor;
            private readonly string word;
            private readonly int revealCost;
            private readonly Button button;
            private bool hidden;

            public bool IsHidden => hidden;

            public TargetChip(Image image, TextMeshProUGUI label, Color normalColor, string word, bool hidden,
                int revealCost, Button button)
            {
                this.image = image;
                this.label = label;
                this.normalColor = normalColor;
                this.word = word;
                this.hidden = hidden;
                this.revealCost = revealCost;
                this.button = button;
                Refresh(false);
            }

            public void Reveal()
            {
                hidden = false;
                Refresh(false);
                image.rectTransform.DOPunchScale(Vector3.one * 0.08f, 0.42f, 5, 0.55f);
            }

            public void SetCompleted(bool completed, bool animate)
            {
                image.DOKill();
                image.color = completed ? Gold : normalColor;
                Refresh(completed);
                if (completed && animate) image.rectTransform.DOPunchScale(Vector3.one * 0.08f, 0.42f, 5, 0.55f);
            }

            private void Refresh(bool completed)
            {
                label.text = completed ? "OK  " + word : hidden ? $"???\n{revealCost} COINS" : word;
                image.color = completed ? Gold : hidden ? Color.Lerp(normalColor, Background, 0.48f) : normalColor;
                button.interactable = hidden && !completed;
            }
        }

        private sealed class MoveRecord
        {
            public Tube Source { get; }
            public Tube Destination { get; }
            public LetterTile Tile { get; }

            public MoveRecord(Tube source, Tube destination, LetterTile tile)
            {
                Source = source;
                Destination = destination;
                Tile = tile;
            }
        }

        private sealed class LevelButtonView
        {
            public int LevelIndex { get; }
            public Button Button { get; }
            public TextMeshProUGUI Label { get; }
            public Image Image { get; }

            public LevelButtonView(int index, Button button, TextMeshProUGUI label, Image image)
            {
                LevelIndex = index;
                Button = button;
                Label = label;
                Image = image;
            }
        }

        private sealed class BackgroundCardView
        {
            public int Index { get; }
            public Image Card { get; }
            public Button Button { get; }
            public TextMeshProUGUI Label { get; }

            public BackgroundCardView(int index, Image card, Button button, TextMeshProUGUI label)
            {
                Index = index;
                Card = card;
                Button = button;
                Label = label;
            }
        }
    }
}
