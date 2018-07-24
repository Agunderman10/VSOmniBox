﻿namespace VSOmniBox.Data
{
    using Microsoft.VisualStudio.Text.PatternMatching;
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using VSOmniBox.API.Data;

    internal sealed class SearchTask : IDisposable
    {
        private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private bool started;

        public static SearchTask Create() => new SearchTask();

        public bool IsDisposed { get; private set; }

        public async Task<SearchDataModel> SearchAsync(
            IEnumerable<(IOmniBoxItemsSource source, IOmniBoxItemsSourceProviderMetadata metadata)> itemsSources,
            IPatternMatcherFactory patternMatcherFactory,
            string searchString)
        {
            if (this.started)
            {
                throw new InvalidOperationException("Search has already started");
            }

            this.CheckCanceled();

            this.started = true;

            var codeResults = await SearchAndSortAndFilterResults(
                SelectPivotProviders(itemsSources, OmniBoxPivot.Code),
                patternMatcherFactory,
                searchString);

            var ideResults = await SearchAndSortAndFilterResults(
                SelectPivotProviders(itemsSources, OmniBoxPivot.IDE),
                patternMatcherFactory,
                searchString);

            return new SearchDataModel(
                codeItems: codeResults,
                ideItems: ideResults);
        }

        public void Dispose()
        {
            if (!this.IsDisposed)
            {
                this.IsDisposed = true;
                this.cancellationTokenSource.Cancel();
                this.cancellationTokenSource.Dispose();
            }
        }

        private IEnumerable<IOmniBoxItemsSource> SelectPivotProviders(
            IEnumerable<(IOmniBoxItemsSource source, IOmniBoxItemsSourceProviderMetadata metadata)> itemsSources,
            OmniBoxPivot pivot)
                => itemsSources
                .Where(tuple => tuple.metadata.Pivot == pivot)
                .Select(tuple => tuple.source);

        private void CheckCanceled()
        {
            if (this.IsDisposed || this.cancellationTokenSource.IsCancellationRequested)
            {
                throw new OperationCanceledException();
            }
        }

        private async Task<ImmutableArray<OmniBoxItem>> SearchAndSortAndFilterResults(
            IEnumerable<IOmniBoxItemsSource> itemsSources,
            IPatternMatcherFactory patternMatcherFactory,
            string searchString)
        {
            var searchProviderTasks = itemsSources.Select(
                itemsSource => new SearchProviderTask(
                    itemsSource,
                    this.cancellationTokenSource.Token)).ToList();

            // Wait for results from all providers from parallel computations on background thread.
            var providerResultsLists = await Task.WhenAll(
                searchProviderTasks.Select(
                    searchProviderTask => Task.Run(() => searchProviderTask.Search(searchString))));

            this.CheckCanceled();

            // Sort and filter on background thread.
            return await Task.Run(
                () => FlattenSortAndFilterLists(patternMatcherFactory, providerResultsLists, searchString));
        }

        private static ImmutableArray<OmniBoxItem> FlattenSortAndFilterLists(
            IPatternMatcherFactory patternMatcherFactory,
            IEnumerable<IEnumerable<OmniBoxItem>> providerResultsLists,
            string searchString)
        {
            // Create pattern matcher for ranking and sorting items.
            var patternMatcherOptions = new PatternMatcherCreationOptions(
                CultureInfo.CurrentCulture,
                PatternMatcherCreationFlags.AllowFuzzyMatching | PatternMatcherCreationFlags.AllowSimpleSubstringMatching);
            var patternMatcher = patternMatcherFactory.CreatePatternMatcher(
                searchString,
                patternMatcherOptions);

            var filteredAndSortedResults = providerResultsLists
                .SelectMany(select => select)
                .Select(result => (item: result, titleMatch: patternMatcher.TryMatch(result.Title), descriptionMatch: patternMatcher.TryMatch(result.Description)))
                .Where(patternMatch => patternMatch.titleMatch != null || patternMatch.descriptionMatch != null)
                .OrderBy(result => result.titleMatch)
                .ThenBy(result => result.descriptionMatch)
                .Select(result => result.item);

            var builder = ImmutableArray.CreateBuilder<OmniBoxItem>();
            builder.AddRange(filteredAndSortedResults);

            return builder.ToImmutable();
        }
    }
}
