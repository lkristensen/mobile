﻿using System;
using Xamarin.Forms;

namespace Bit.App.Controls
{
    public class ExtendedTabbedPage : TabbedPage
    {
        public ExtendedTabbedPage()
        {
            BackgroundColor = Color.FromHex("efeff4");
        }

        public static readonly BindableProperty TintColorProperty =
            BindableProperty.Create(nameof(TintColor), typeof(Color), typeof(ExtendedTabbedPage), Color.White);

        public static readonly BindableProperty BarTintColorProperty =
            BindableProperty.Create(nameof(BarTintColor), typeof(Color), typeof(ExtendedTabbedPage), Color.White);

        public Color TintColor
        {
            get { return (Color)GetValue(TintColorProperty); }
            set { SetValue(TintColorProperty, value); }
        }

        public Color BarTintColor
        {
            get { return (Color)GetValue(BarTintColorProperty); }
            set { SetValue(BarTintColorProperty, value); }
        }
    }
}
