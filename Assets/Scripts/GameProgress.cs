using UnityEngine;

namespace WordSortPuzzle
{
    internal static class GameProgress
    {
        private const string HighestLevelKey = "word_sort.highest_level";
        private const string CurrentLevelKey = "word_sort.current_level";
        private const string SoundKey = "word_sort.sound";
        private const string MusicKey = "word_sort.music";
        private const string VibrationKey = "word_sort.vibration";
        private const string CoinsKey = "word_sort.coins";
        private const string UndoTokensKey = "word_sort.undo_tokens";
        private const string SelectedBackgroundKey = "word_sort.selected_background";
        private const int StartingCoins = 100;
        private static readonly int[] BackgroundUnlockLevels = { 0, 10, 25, 50, 100 };

        public static int HighestUnlocked
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(HighestLevelKey, 0), 0, WordLevelCatalog.TotalLevels - 1);
            private set => PlayerPrefs.SetInt(HighestLevelKey, Mathf.Clamp(value, 0, WordLevelCatalog.TotalLevels - 1));
        }

        public static int CurrentLevel
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(CurrentLevelKey, 0), 0, WordLevelCatalog.TotalLevels - 1);
            set
            {
                PlayerPrefs.SetInt(CurrentLevelKey, Mathf.Clamp(value, 0, WordLevelCatalog.TotalLevels - 1));
                PlayerPrefs.Save();
            }
        }

        public static bool Sound
        {
            get => PlayerPrefs.GetInt(SoundKey, 1) == 1;
            set => SetBool(SoundKey, value);
        }

        public static bool Music
        {
            get => PlayerPrefs.GetInt(MusicKey, 1) == 1;
            set => SetBool(MusicKey, value);
        }

        public static bool Vibration
        {
            get => PlayerPrefs.GetInt(VibrationKey, 1) == 1;
            set => SetBool(VibrationKey, value);
        }

        public static int Coins
        {
            get => Mathf.Max(0, PlayerPrefs.GetInt(CoinsKey, StartingCoins));
            private set
            {
                PlayerPrefs.SetInt(CoinsKey, Mathf.Max(0, value));
                PlayerPrefs.Save();
            }
        }

        public static int UndoTokens
        {
            get => Mathf.Max(0, PlayerPrefs.GetInt(UndoTokensKey, 0));
            private set
            {
                PlayerPrefs.SetInt(UndoTokensKey, Mathf.Max(0, value));
                PlayerPrefs.Save();
            }
        }

        public static int SelectedBackground
        {
            get => Mathf.Max(0, PlayerPrefs.GetInt(SelectedBackgroundKey, 0));
            set
            {
                PlayerPrefs.SetInt(SelectedBackgroundKey, Mathf.Max(0, value));
                PlayerPrefs.Save();
            }
        }

        public static void AddCoins(int amount)
        {
            if (amount <= 0) return;
            Coins += amount;
        }

        public static bool TrySpendCoins(int amount)
        {
            if (amount < 0 || Coins < amount) return false;
            Coins -= amount;
            return true;
        }

        public static void AddUndoTokens(int amount)
        {
            if (amount <= 0) return;
            UndoTokens += amount;
        }

        public static bool TrySpendUndoToken()
        {
            if (UndoTokens <= 0) return false;
            UndoTokens--;
            return true;
        }

        public static bool IsBonusTubeUnlocked(int levelIndex)
        {
            return PlayerPrefs.GetInt(BonusTubeKey(levelIndex), 0) == 1;
        }

        public static void UnlockBonusTube(int levelIndex)
        {
            PlayerPrefs.SetInt(BonusTubeKey(levelIndex), 1);
            PlayerPrefs.Save();
        }

        public static bool IsWordRevealed(int levelIndex, int wordIndex)
        {
            return PlayerPrefs.GetInt(RevealedWordKey(levelIndex, wordIndex), 0) == 1;
        }

        public static void RevealWord(int levelIndex, int wordIndex)
        {
            PlayerPrefs.SetInt(RevealedWordKey(levelIndex, wordIndex), 1);
            PlayerPrefs.Save();
        }

        public static bool IsBackgroundUnlocked(int backgroundIndex)
        {
            if (backgroundIndex < 0 || backgroundIndex >= BackgroundUnlockLevels.Length) return false;
            int requiredLevel = BackgroundUnlockLevels[backgroundIndex];
            return requiredLevel == 0 || IsCompleted(requiredLevel - 1);
        }

        public static int BackgroundUnlockLevel(int backgroundIndex)
        {
            if (backgroundIndex < 0 || backgroundIndex >= BackgroundUnlockLevels.Length) return int.MaxValue;
            return BackgroundUnlockLevels[backgroundIndex];
        }

        public static bool IsCompleted(int levelIndex)
        {
            return PlayerPrefs.GetInt(BestMovesKey(levelIndex), 0) > 0;
        }

        public static int BestMoves(int levelIndex)
        {
            return PlayerPrefs.GetInt(BestMovesKey(levelIndex), 0);
        }

        public static void CompleteLevel(int levelIndex, int moves)
        {
            string key = BestMovesKey(levelIndex);
            int previous = PlayerPrefs.GetInt(key, 0);
            if (previous == 0 || moves < previous)
            {
                PlayerPrefs.SetInt(key, Mathf.Max(1, moves));
            }

            if (levelIndex < WordLevelCatalog.TotalLevels - 1)
            {
                HighestUnlocked = Mathf.Max(HighestUnlocked, levelIndex + 1);
                CurrentLevel = levelIndex + 1;
            }
            PlayerPrefs.Save();
        }

        private static string BestMovesKey(int levelIndex) => "word_sort.best." + levelIndex;

        private static string BonusTubeKey(int levelIndex) => "word_sort.bonus_tube." + levelIndex;

        private static string RevealedWordKey(int levelIndex, int wordIndex) =>
            $"word_sort.revealed_word.{levelIndex}.{wordIndex}";

        private static void SetBool(string key, bool value)
        {
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
