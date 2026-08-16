using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Entities;

namespace TheIntroDB.Services
{
    public static class ChapterMarkerPolicy
    {
        public const string TheIntroDbTag = " (TheIntroDB)";
        private const string OwnershipPrefix = " [TheIntroDB:";
        private const string OwnershipSuffix = "]";

        public static string AddOwnershipToken(string name, string ownerToken)
        {
            if (string.IsNullOrWhiteSpace(ownerToken))
            {
                throw new ArgumentException("Ownership token is required.", nameof(ownerToken));
            }

            return (name ?? string.Empty) + OwnershipPrefix + ownerToken + OwnershipSuffix;
        }

        public static bool HasOwnershipToken(string name, string ownerToken)
        {
            return !string.IsNullOrWhiteSpace(ownerToken) &&
                !string.IsNullOrEmpty(name) &&
                name.EndsWith(OwnershipPrefix + ownerToken + OwnershipSuffix, StringComparison.Ordinal);
        }

        public static bool HasOwnedLabel(string name, string label)
        {
            var prefix = (label ?? string.Empty) + OwnershipPrefix;
            return !string.IsNullOrEmpty(name) &&
                name.Length > prefix.Length + OwnershipSuffix.Length &&
                name.StartsWith(prefix, StringComparison.Ordinal) &&
                name.EndsWith(OwnershipSuffix, StringComparison.Ordinal);
        }

        /// <summary>
        /// True when the chapter name is an exact legacy TheIntroDB tag written by
        /// pre-token releases (e.g. "Recap (TheIntroDB)"). These carry no ownership
        /// token and are recognized so the one-time adoption pass can claim them.
        /// </summary>
        public static bool HasLegacyTaggedName(string name, string label)
        {
            return !string.IsNullOrEmpty(name) &&
                string.Equals(name, label + TheIntroDbTag, StringComparison.Ordinal);
        }

        public static bool HasNativeIntroMarker(IReadOnlyCollection<ChapterInfo> chapters)
        {
            return chapters.Any(c =>
                c.MarkerType == MarkerType.IntroStart || c.MarkerType == MarkerType.IntroEnd);
        }

        public static bool HasNativeCreditsMarker(IReadOnlyCollection<ChapterInfo> chapters)
        {
            return chapters.Any(c => c.MarkerType == MarkerType.CreditsStart);
        }
    }
}
