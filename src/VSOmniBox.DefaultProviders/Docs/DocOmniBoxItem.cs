﻿using System.Diagnostics;
using VSOmniBox.API.Data;

namespace VSOmniBox.DefaultProviders.Docs
{
    internal class DocOmniBoxItem : OmniBoxItem
    {
        private readonly string link;
        public DocOmniBoxItem(string title, string desc, string link)
        {
            Title = title;
            Description = desc;
            this.link = link;
        }

        public override string Title { get; }

        public override string Description { get; }

        public override void Invoke()
        {
            Process.Start(link);
        }
    }
}