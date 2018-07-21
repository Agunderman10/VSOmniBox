﻿namespace VSOmniBox.DefaultProviders.QuickLaunch
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.VisualStudio;
    using Microsoft.VisualStudio.Shell.Interop;

    public class SearchQueryShim : IVsSearchQuery
    {
        private static readonly char[] TokenSeparators = { ' ', ',', '.' };

        // Lazily initialized.
        private IVsSearchToken[] tokens;

        public SearchQueryShim(string searchString)
        {
            if (string.IsNullOrWhiteSpace(searchString))
            {
                throw new ArgumentException("Argument cannot be null or whitespace", nameof(searchString));
            }

            this.SearchString = searchString;
        }

        public uint GetTokens(uint dwMaxTokens, IVsSearchToken[] rgpSearchTokens)
        {
            if (dwMaxTokens < 0)
            {
                throw new ArgumentException("Cannot be less than zero", nameof(dwMaxTokens));
            }
            else if (dwMaxTokens == 0)
            {
                return (uint)this.Tokens.Length;
            }

            if (rgpSearchTokens.Length < dwMaxTokens)
            {
                throw new ArgumentOutOfRangeException("Not enough buffer for requested count", nameof(rgpSearchTokens));
            }

            var tokens = this.Tokens;

            Array.Copy(tokens, rgpSearchTokens, dwMaxTokens);

            return Math.Min(dwMaxTokens, (uint)tokens.Length);
        }

        public string SearchString { get;  }

        public uint ParseError => VSConstants.S_OK;

        public IVsSearchToken[] Tokens => this.tokens ?? (this.tokens = this.ParseTokens().ToArray());

        /// <summary>
        /// For now, we're parsing the search string, creating tokens for each of the individual
        /// symbols or camel-case words in the string so that we hopefully match any potentials.
        /// The pattern matcher is responsible for filtering down to the final well matching set.
        /// </summary>
        /// <returns></returns>
        private IEnumerable<IVsSearchToken> ParseTokens()
        {
            for (int start = 0; start < this.SearchString.Length; start++)
            {
                char startChar = this.SearchString[start];

                if (!char.IsWhiteSpace(startChar))
                {
                    if (char.IsSymbol(startChar))
                    {
                        yield return new SearchTokenShim(new string(startChar, 1), (uint)start);
                    }
                    else if (char.IsUpper(startChar) || (start > 0 && char.IsWhiteSpace(this.SearchString[start - 1]) || char.IsSymbol(this.SearchString[start - 1])))
                    {
                        int end = start + 1;
                        for (; end < this.SearchString.Length && char.IsLetterOrDigit(this.SearchString[end]) && char.IsLower(this.SearchString[end]); end++);

                        var tokenString = this.SearchString.Substring(start, end - start);
                        if (tokenString.Length > 0 &&
                            char.IsUpper(tokenString[0]) &&
                            ((tokenString.Length == 1) || char.IsLower(tokenString[tokenString.Length - 1])))
                        {
                            yield return new SearchTokenShim(tokenString, (uint)start);
                        }
                    }
                }
            }
        }
    }
}
