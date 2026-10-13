# Monetization setup

The game code is complete; replace the placeholders in
`Assets/Resources/monetization-config.json` before making a store build.

LevelPlay ad mediation is compiled and initialized only for non-WebGL targets.
WebGL builds, including YouTube Playables builds, skip mediation and continue
without rewarded, interstitial, or banner ads.

Backgrounds are unlocked by completed levels: Mystic Library after level 10,
Moon Observatory after level 25, Enchanted Garden after level 50, and Neon
Alchemy after level 100.

## Unity LevelPlay

1. In the Unity Dashboard, create/select the Android game and copy its LevelPlay app key.
2. Create one rewarded, one interstitial, and one banner ad unit.
3. Paste those four values into `monetization-config.json`.
4. In **LevelPlay > Network Manager**, install and configure the networks you want to mediate.
5. Configure each network's app/ad-unit credentials in the LevelPlay dashboard and resolve Android dependencies.
6. Use test mode and test devices until the Play Console release is ready. Never click live ads yourself.

The current placements are:

- Rewarded: optional `+75 coins` button in the coin shop.
- Interstitial: on every completed-level transition, when the player taps Next.
- Banner: bottom-center throughout menus, selection screens, and gameplay.
- Buying `word_sort.remove_ads` disables every ad format permanently.

## Google Play products

Create these product IDs in Play Console (or change both Play Console and the JSON to your preferred IDs):

- `word_sort.coins.small` — consumable, grants 100 coins.
- `word_sort.coins.medium` — consumable, grants 500 coins.
- `word_sort.coins.large` — consumable, grants 1200 coins.
- `word_sort.remove_ads` — non-consumable.

Activate the products, upload a signed App Bundle to an internal test track, add license testers, and test with a tester account. Store-localized prices automatically replace the BUY button labels.

The client prevents accidental repeat fulfillment using transaction IDs. Before processing real-money purchases at scale, validate receipts on a trusted backend; the source marks the exact validation point.

## Privacy

Host your final privacy policy at a public HTTPS URL and put it in `privacyPolicyUrl` in the JSON. The first launch blocks ad initialization until the player chooses personalized or non-personalized ads. The choice is saved and can be changed under **Settings > Privacy Choices**.

This consent screen passes the selected GDPR consent value to LevelPlay, but it is not legal advice. Your published policy and Play Console Data safety form must accurately list every mediated ad network and its data practices. If your selected networks or target regions require a Google-certified CMP/UMP flow, enable that CMP in LevelPlay in addition to this in-game explanation and settings entry.
