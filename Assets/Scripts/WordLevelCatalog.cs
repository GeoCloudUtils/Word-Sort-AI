using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WordSortPuzzle
{
    [Serializable]
    internal sealed class WordDictionaryData
    {
        public string[] words3;
        public string[] words4;
        public string[] words5;
        public string[] words6;
    }

    internal sealed class WordLevelDefinition
    {
        public int Index { get; }
        public int Number => Index + 1;
        public int WordLength { get; }
        public int WordCount { get; }
        // Two empty helper tubes keep every puzzle maneuverable after a completed
        // word is locked. This mirrors the breathing room of Ball Sort Puzzle.
        public int TubeCount => WordCount + 2;
        public int ScrambleMoves { get; }
        public int Seed { get; }
        public IReadOnlyList<string> Words { get; }

        public WordLevelDefinition(int index, int wordLength, int wordCount, int scrambleMoves, int seed, IReadOnlyList<string> words)
        {
            Index = index;
            WordLength = wordLength;
            WordCount = wordCount;
            ScrambleMoves = scrambleMoves;
            Seed = seed;
            Words = words;
        }
    }

    internal static class WordLevelCatalog
    {
        public const int LevelsPerLength = 50;
        public const int TotalLevels = 200;

        private static readonly Dictionary<int, string[]> WordsByLength = LoadDictionary();

        public static WordLevelDefinition Get(int levelIndex)
        {
            levelIndex = Mathf.Clamp(levelIndex, 0, TotalLevels - 1);
            int group = levelIndex / LevelsPerLength;
            int inGroup = levelIndex % LevelsPerLength;
            int length = group + 3;
            int wordCount = GetWordCount(inGroup);
            string[] bank = WordsByLength[length];
            List<string> selected = new List<string>(wordCount);

            // Prime-number steps produce stable but varied combinations across all 50 levels.
            int start = (inGroup * 13 + group * 23) % bank.Length;
            int cursor = start;
            while (selected.Count < wordCount)
            {
                string candidate = bank[cursor % bank.Length];
                if (!selected.Contains(candidate))
                {
                    selected.Add(candidate);
                }
                cursor += 17;
            }

            int scrambleMoves = 16 + inGroup * 2 + wordCount * length;
            int seed = 7919 * (levelIndex + 1) + 104729;
            return new WordLevelDefinition(levelIndex, length, wordCount, scrambleMoves, seed, selected);
        }

        private static int GetWordCount(int inGroup)
        {
            if (inGroup < 5) return 2;   // 4 tubes
            if (inGroup < 15) return 3;  // 5 tubes
            if (inGroup < 30) return 4;  // 6 tubes
            return 5;                    // 7 tubes
        }

        private static Dictionary<int, string[]> LoadDictionary()
        {
            TextAsset asset = Resources.Load<TextAsset>("word-dictionary");
            if (asset == null)
            {
                throw new InvalidOperationException("Resources/word-dictionary.json is missing.");
            }

            WordDictionaryData data = JsonUtility.FromJson<WordDictionaryData>(asset.text);
            Dictionary<int, string[]> result = new Dictionary<int, string[]>
            {
                { 3, Clean(data.words3, 3) },
                { 4, Clean(data.words4, 4) },
                { 5, Clean(data.words5, 5) },
                { 6, Clean(data.words6, 6) }
            };

            foreach (KeyValuePair<int, string[]> entry in result)
            {
                if (entry.Value.Length < LevelsPerLength)
                {
                    throw new InvalidOperationException($"The {entry.Key}-letter dictionary needs at least {LevelsPerLength} valid unique words.");
                }
            }

            return result;
        }

        private static string[] Clean(IEnumerable<string> words, int length)
        {
            if (words == null) return Array.Empty<string>();
            return words
                .Where(word => !string.IsNullOrWhiteSpace(word))
                .Select(word => word.Trim().ToUpperInvariant())
                .Where(word => word.Length == length && word.All(character => character >= 'A' && character <= 'Z'))
                .Distinct()
                .ToArray();
        }
    }
}
