using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Purchasing;
#if !UNITY_WEBGL
using Unity.Services.LevelPlay;
#endif

namespace WordSortPuzzle
{
    /// <summary>
    /// Owns Unity IAP and LevelPlay. Configuration is loaded from
    /// Resources/monetization-config.json so store/ad IDs never need to be hard-coded in UI logic.
    /// </summary>
    public sealed class MonetizationManager : MonoBehaviour
    {
        [Serializable]
        public sealed class Configuration
        {
            public string androidLevelPlayAppKey;
            public string iosLevelPlayAppKey;
            public string rewardedAdUnitId;
            public string interstitialAdUnitId;
            public string bannerAdUnitId;
            public string smallCoinsProductId = "word_sort.coins.small";
            public string mediumCoinsProductId = "word_sort.coins.medium";
            public string largeCoinsProductId = "word_sort.coins.large";
            public string removeAdsProductId = "word_sort.remove_ads";
            public string privacyPolicyUrl;
            public int smallCoins = 100;
            public int mediumCoins = 500;
            public int largeCoins = 1200;
            public int rewardedCoins = 75;
            public int interstitialEveryCompletedLevels = 1;
            public int interstitialEveryLevelResets = 3;
            public float interstitialMinimumSecondsBetweenAds = 90f;
            public float interstitialSessionGraceSeconds = 45f;
        }

        private const string ConfigResource = "monetization-config";
        private const string ConsentChoiceKey = "word_sort.privacy.ads_consent";
        private const string NoAdsKey = "word_sort.iap.no_ads";
        private const string FulfilledPrefix = "word_sort.iap.fulfilled.";

        private StoreController store;
#if !UNITY_WEBGL
        private LevelPlayRewardedAd rewardedAd;
        private LevelPlayInterstitialAd interstitialAd;
        private LevelPlayBannerAd bannerAd;
#endif
        private Action pendingReward;
        private Action pendingInterstitialCompletion;
        private bool initialized;
        private bool adsInitialized;
        private bool bannerShouldBeVisible;
        private bool purchasingSettled;
        private bool adsSettled;
        private int completedLevelsSinceInterstitial;
        private int levelResetsSinceInterstitial;
        private float sessionStartedAt;
        private float lastInterstitialShownAt = float.NegativeInfinity;

        public Configuration Config { get; private set; }
        public bool HasConsentChoice => PlayerPrefs.HasKey(ConsentChoiceKey);
        public bool PersonalizedAdsAllowed => PlayerPrefs.GetInt(ConsentChoiceKey, 0) == 1;
        public bool AdsRemoved => PlayerPrefs.GetInt(NoAdsKey, 0) == 1;
        public bool RewardedReady
        {
            get
            {
#if !UNITY_WEBGL
                return rewardedAd != null && rewardedAd.IsAdReady();
#else
                return false;
#endif
            }
        }
        public bool StoreReady { get; private set; }
        public bool InitializationStarted => initialized;
        public bool InitializationComplete { get; private set; }

        public event Action<string> StatusChanged;
        public event Action ProductsChanged;
        public event Action BalancesChanged;
        public event Action<string, float> InitializationProgressChanged;
        public event Action InitializationCompleted;

        private void Awake()
        {
            sessionStartedAt = Time.realtimeSinceStartup;
            TextAsset asset = Resources.Load<TextAsset>(ConfigResource);
            Config = asset == null ? new Configuration() : JsonUtility.FromJson<Configuration>(asset.text);
            if (Config == null) Config = new Configuration();
        }

        public void SaveConsentChoice(bool allowPersonalizedAds)
        {
            PlayerPrefs.SetInt(ConsentChoiceKey, allowPersonalizedAds ? 1 : 0);
            PlayerPrefs.Save();
#if !UNITY_WEBGL
            if (adsInitialized) LevelPlayPrivacySettings.SetGDPRConsent(allowPersonalizedAds);
#endif
        }

        public void InitializeServices()
        {
            if (initialized) return;
#if !UNITY_WEBGL
            if (!HasConsentChoice) return;
#endif
            initialized = true;
            SetInitializationProgress("CONNECTING TO STORE", 0.42f);
#if UNITY_WEBGL
            SetPurchasingSettled("PURCHASES DISABLED FOR WEBGL");
#else
            InitializePurchasing();
#endif
            InitializeAds();
        }

        public string GetLocalizedPrice(string productId, string fallback)
        {
            Product product = store?.GetProducts()?.FirstOrDefault(item => item.definition.id == productId);
            return product != null && product.availableToPurchase
                ? product.metadata.localizedPriceString
                : fallback;
        }

        public void BuyProduct(string productId)
        {
            if (!StoreReady || store == null)
            {
                Report("STORE IS STILL CONNECTING");
                return;
            }

            Product product = store.GetProducts().FirstOrDefault(item => item.definition.id == productId);
            if (product == null || !product.availableToPurchase)
            {
                Report("PRODUCT IS NOT AVAILABLE");
                return;
            }

            Report("OPENING GOOGLE PLAY...");
            store.PurchaseProduct(product);
        }

        public void RestorePurchases()
        {
            if (store == null)
            {
                Report("STORE IS STILL CONNECTING");
                return;
            }

            store.RestoreTransactions((success, error) =>
                Report(success ? "PURCHASES RESTORED" : "RESTORE FAILED: " + error));
        }

        public void ShowRewardedCoins()
        {
#if UNITY_WEBGL
            Report("REWARDED ADS ARE UNAVAILABLE IN WEBGL");
            return;
#else
            if (AdsRemoved)
            {
                Report("REWARDED ADS ARE DISABLED");
                return;
            }

            if (!RewardedReady)
            {
                Report("REWARDED AD IS NOT READY YET");
                rewardedAd?.LoadAd();
                return;
            }

            pendingReward = () =>
            {
                GameProgress.AddCoins(Mathf.Max(1, Config.rewardedCoins));
                BalancesChanged?.Invoke();
                Report($"+{Mathf.Max(1, Config.rewardedCoins)} COINS");
            };
            rewardedAd.ShowAd("shop_rewarded_coins");
#endif
        }

        public void ShowInterstitialIfDue(Action onComplete)
        {
            completedLevelsSinceInterstitial++;
            int interval = Mathf.Max(1, Config.interstitialEveryCompletedLevels);
#if UNITY_WEBGL
            onComplete?.Invoke();
            return;
#else
            if (completedLevelsSinceInterstitial < interval || !CanShowInterstitial())
            {
                onComplete?.Invoke();
                return;
            }

            ShowInterstitial("level_complete", onComplete);
#endif
        }

        public void ShowInterstitialAfterLevelReset(Action onComplete)
        {
            levelResetsSinceInterstitial++;
            int interval = Mathf.Max(2, Config.interstitialEveryLevelResets);
#if UNITY_WEBGL
            onComplete?.Invoke();
            return;
#else
            if (levelResetsSinceInterstitial < interval || !CanShowInterstitial())
            {
                onComplete?.Invoke();
                return;
            }

            ShowInterstitial("repeated_level_reset", onComplete);
#endif
        }

#if !UNITY_WEBGL
        private bool CanShowInterstitial()
        {
            if (AdsRemoved || pendingInterstitialCompletion != null || interstitialAd == null ||
                !interstitialAd.IsAdReady())
                return false;

            float now = Time.realtimeSinceStartup;
            float grace = Mathf.Max(0f, Config.interstitialSessionGraceSeconds);
            float cooldown = Mathf.Max(0f, Config.interstitialMinimumSecondsBetweenAds);
            return now - sessionStartedAt >= grace && now - lastInterstitialShownAt >= cooldown;
        }

        private void ShowInterstitial(string placement, Action onComplete)
        {
            completedLevelsSinceInterstitial = 0;
            levelResetsSinceInterstitial = 0;
            lastInterstitialShownAt = Time.realtimeSinceStartup;
            pendingInterstitialCompletion = onComplete;
            interstitialAd.ShowAd(placement);
        }
#endif

        public void SetBannerVisible(bool visible)
        {
            bannerShouldBeVisible = visible;
#if !UNITY_WEBGL
            if (bannerAd == null || AdsRemoved) return;
            if (visible) bannerAd.ShowAd();
            else bannerAd.HideAd();
#endif
        }

        public void OpenPrivacyPolicy()
        {
            if (string.IsNullOrWhiteSpace(Config.privacyPolicyUrl) || Config.privacyPolicyUrl.Contains("YOUR_"))
            {
                Report("ADD YOUR PRIVACY POLICY URL IN monetization-config.json");
                return;
            }
            Application.OpenURL(Config.privacyPolicyUrl);
        }

        private async void InitializePurchasing()
        {
            store = UnityIAPServices.StoreController();
            store.OnStoreConnected += OnStoreConnected;
            store.OnStoreDisconnected += failure =>
            {
                Report("STORE CONNECTION FAILED: " + failure.Message);
                SetPurchasingSettled("STORE UNAVAILABLE - OFFLINE MODE");
            };
            store.OnProductsFetched += OnProductsFetched;
            store.OnProductsFetchFailed += failure =>
            {
                Report("PRODUCT LOAD FAILED: " + failure.FailureReason);
                SetPurchasingSettled("PRODUCT CATALOG UNAVAILABLE");
            };
            store.OnPurchasesFetched += OnPurchasesFetched;
            store.OnPurchasesFetchFailed += failure => Debug.LogWarning("[IAP] Purchase restore query failed: " + failure.Message);
            store.OnPurchasePending += OnPurchasePending;
            store.OnPurchaseFailed += order => Report("PURCHASE FAILED: " + order.FailureReason);
            store.OnPurchaseConfirmed += OnPurchaseConfirmed;

            try
            {
                await store.Connect();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Report("STORE CONNECTION FAILED");
                SetPurchasingSettled("STORE UNAVAILABLE - OFFLINE MODE");
            }
        }

        private void OnStoreConnected()
        {
            var products = new List<ProductDefinition>
            {
                new ProductDefinition(Config.smallCoinsProductId, ProductType.Consumable),
                new ProductDefinition(Config.mediumCoinsProductId, ProductType.Consumable),
                new ProductDefinition(Config.largeCoinsProductId, ProductType.Consumable),
                new ProductDefinition(Config.removeAdsProductId, ProductType.NonConsumable)
            };
            store.FetchProducts(products);
        }

        private void OnProductsFetched(List<Product> products)
        {
            StoreReady = true;
            ProductsChanged?.Invoke();
            store.FetchPurchases();
            Report("STORE READY");
            SetPurchasingSettled("PURCHASE CATALOG READY");
        }

        private void OnPurchasesFetched(Orders orders)
        {
            bool ownsNoAds = orders.ConfirmedOrders.Any(OrderContainsRemoveAds) ||
                             orders.PendingOrders.Any(OrderContainsRemoveAds);
            if (ownsNoAds) GrantRemoveAds();
        }

        private bool OrderContainsRemoveAds(Order order) =>
            order.CartOrdered.Items().Any(item => item.Product.definition.id == Config.removeAdsProductId);

        private void OnPurchasePending(PendingOrder order)
        {
            string transactionId = order.Info.TransactionID;
            bool alreadyFulfilled = !string.IsNullOrEmpty(transactionId) &&
                                    PlayerPrefs.GetInt(FulfilledPrefix + transactionId, 0) == 1;

            if (!alreadyFulfilled)
            {
                foreach (CartItem item in order.CartOrdered.Items()) Fulfill(item.Product.definition.id);
                if (!string.IsNullOrEmpty(transactionId)) PlayerPrefs.SetInt(FulfilledPrefix + transactionId, 1);
                PlayerPrefs.Save();
            }

            // For a production economy, validate order.Info receipt on a trusted server before this call.
            store.ConfirmPurchase(order);
        }

        private void Fulfill(string productId)
        {
            if (productId == Config.smallCoinsProductId) GameProgress.AddCoins(Config.smallCoins);
            else if (productId == Config.mediumCoinsProductId) GameProgress.AddCoins(Config.mediumCoins);
            else if (productId == Config.largeCoinsProductId) GameProgress.AddCoins(Config.largeCoins);
            else if (productId == Config.removeAdsProductId) GrantRemoveAds();
            else return;

            BalancesChanged?.Invoke();
            Report("PURCHASE DELIVERED");
        }

        private void GrantRemoveAds()
        {
            PlayerPrefs.SetInt(NoAdsKey, 1);
            PlayerPrefs.Save();
#if !UNITY_WEBGL
            bannerAd?.HideAd();
#endif
            BalancesChanged?.Invoke();
        }

        private void OnPurchaseConfirmed(Order order)
        {
            if (order is FailedOrder failed) Report("PURCHASE CONFIRMATION FAILED: " + failed.FailureReason);
        }

        private void InitializeAds()
        {
#if UNITY_WEBGL
            SetAdsSettled("AD MEDIATION DISABLED FOR WEBGL");
            return;
#else
            SetInitializationProgress("INITIALIZING AD MEDIATION", 0.68f);
            if (AdsRemoved)
            {
                SetAdsSettled("ADS DISABLED BY PURCHASE");
                return;
            }
#if UNITY_IOS
            string appKey = Config.iosLevelPlayAppKey;
#else
            string appKey = Config.androidLevelPlayAppKey;
#endif
            if (!IsConfigured(appKey))
            {
                Debug.LogWarning("[Ads] Add the LevelPlay app key and ad unit IDs to Resources/monetization-config.json.");
                SetAdsSettled("ADS WAITING FOR DASHBOARD CONFIG");
                return;
            }

            LevelPlayPrivacySettings.SetGDPRConsent(PersonalizedAdsAllowed);
            LevelPlay.OnInitSuccess += OnAdsInitialized;
            LevelPlay.OnInitFailed += OnAdsInitializationFailed;
            LevelPlay.Init(appKey);
#endif
        }

#if !UNITY_WEBGL
        private void OnAdsInitializationFailed(LevelPlayInitError error)
        {
            Debug.LogError("[Ads] LevelPlay initialization failed: " + error);
            SetAdsSettled("ADS UNAVAILABLE - CONTINUING");
        }

        private void OnAdsInitialized(LevelPlayConfiguration configuration)
        {
            adsInitialized = true;
            if (IsConfigured(Config.rewardedAdUnitId))
            {
                rewardedAd = new LevelPlayRewardedAd(Config.rewardedAdUnitId);
                rewardedAd.OnAdRewarded += (info, reward) =>
                {
                    Action grant = pendingReward;
                    pendingReward = null;
                    grant?.Invoke();
                };
                rewardedAd.OnAdClosed += info => rewardedAd.LoadAd();
                rewardedAd.OnAdDisplayFailed += (info, error) =>
                {
                    pendingReward = null;
                    Report("AD COULD NOT BE SHOWN");
                    rewardedAd.LoadAd();
                };
                rewardedAd.OnAdLoadFailed += error => Debug.LogWarning("[Ads] Rewarded load failed: " + error);
                rewardedAd.LoadAd();
            }

            if (IsConfigured(Config.interstitialAdUnitId))
            {
                interstitialAd = new LevelPlayInterstitialAd(Config.interstitialAdUnitId);
                interstitialAd.OnAdClosed += info => CompleteInterstitial();
                interstitialAd.OnAdDisplayFailed += (info, error) => CompleteInterstitial();
                interstitialAd.OnAdLoadFailed += error => Debug.LogWarning("[Ads] Interstitial load failed: " + error);
                interstitialAd.LoadAd();
            }

            if (IsConfigured(Config.bannerAdUnitId))
            {
                var bannerConfig = new LevelPlayBannerAd.Config.Builder()
                    .SetSize(LevelPlayAdSize.BANNER)
                    .SetPosition(LevelPlayBannerPosition.BottomCenter)
                    .SetRespectSafeArea(true)
                    .SetDisplayOnLoad(false)
                    .Build();
                bannerAd = new LevelPlayBannerAd(Config.bannerAdUnitId, bannerConfig);
                bannerAd.OnAdLoaded += info =>
                {
                    if (bannerShouldBeVisible && !AdsRemoved) bannerAd.ShowAd();
                };
                bannerAd.LoadAd();
            }

            SetAdsSettled("AD MEDIATION READY");
        }

#endif

        private void SetPurchasingSettled(string message)
        {
            if (purchasingSettled) return;
            purchasingSettled = true;
            SetInitializationProgress(message, adsSettled ? 1f : 0.72f);
            CheckInitializationCompleted();
        }

        private void SetAdsSettled(string message)
        {
            if (adsSettled) return;
            adsSettled = true;
            SetInitializationProgress(message, purchasingSettled ? 1f : 0.82f);
            CheckInitializationCompleted();
        }

        private void SetInitializationProgress(string message, float progress)
        {
            InitializationProgressChanged?.Invoke(message, Mathf.Clamp01(progress));
        }

        private void CheckInitializationCompleted()
        {
            if (InitializationComplete || !purchasingSettled || !adsSettled) return;
            InitializationComplete = true;
            SetInitializationProgress("ALL SYSTEMS READY", 1f);
            InitializationCompleted?.Invoke();
        }

#if !UNITY_WEBGL
        private void CompleteInterstitial()
        {
            interstitialAd?.LoadAd();
            Action completion = pendingInterstitialCompletion;
            pendingInterstitialCompletion = null;
            completion?.Invoke();
        }
#endif

        private static bool IsConfigured(string value) =>
            !string.IsNullOrWhiteSpace(value) && !value.Contains("YOUR_");

        private void Report(string message)
        {
            Debug.Log("[Monetization] " + message);
            StatusChanged?.Invoke(message);
        }

        private void OnDestroy()
        {
#if !UNITY_WEBGL
            LevelPlay.OnInitSuccess -= OnAdsInitialized;
            LevelPlay.OnInitFailed -= OnAdsInitializationFailed;
            rewardedAd?.DestroyAd();
            interstitialAd?.DestroyAd();
            bannerAd?.DestroyAd();
#endif
        }
    }
}
