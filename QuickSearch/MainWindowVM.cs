using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using QuickSearch.Properties;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace QuickSearch
{
    internal partial class SearchResultEntry : ObservableObject
    {
        [ObservableProperty]
        private string fullName;

        [ObservableProperty]
        private string extension;

        [ObservableProperty]
        private string path;

        [ObservableProperty]
        private string size;

        public string FullPath;

        public SearchResultEntry()
        {
            FullName = string.Empty;
            Extension = string.Empty;
            Path = string.Empty;
            Size = string.Empty;
            FullPath = string.Empty;
        }
    }

    internal class SearchOptionsContainer
    {
        public string RootPath;
        public string SearchString;
        public int MatchType;
        public bool NoSubdirectories;
        public bool CaseSensitive;

        public SearchOptionsContainer()
        {
            RootPath = string.Empty;
            SearchString = string.Empty;
        }
    }

    internal partial class MainWindowVM : ObservableObject
    {
        // Properties
        [ObservableProperty]
        private string searchString;

        [ObservableProperty]
        private string startPath;

        [ObservableProperty]
        private bool doNotSearchSubfolders;

        [ObservableProperty]
        private bool isCaseSensitive;

        [ObservableProperty]
        private int matchType;

        [ObservableProperty]
        private bool isRunning;

        [ObservableProperty]
        private string statusLeft;

        [ObservableProperty]
        private string statusCenter;


        [ObservableProperty]
        private string searchResultTooltip;

        [ObservableProperty]
        private string searchTooltip;

        [ObservableProperty]
        private string matchToolTip;


        public ObservableCollection<SearchResultEntry> SearchResults { get; private set; }

        private CancellationTokenSource HaltRequest;


        // Commands
        public RelayCommand StartCmd { get; }
        public RelayCommand StopCmd { get; }
        public RelayCommand OpenFolderCmd { get; }
        public RelayCommand<object?> DoubleClickCmd { get; }

        // Code
        public MainWindowVM()
        {
            StartPath = string.Empty;
            SearchString = string.Empty;
            StatusLeft = string.Empty;
            StatusCenter = string.Empty;

            StartPath = Settings.Default.SearchPath;
            SearchString = Settings.Default.SearchString;
            MatchType = Settings.Default.MatchType;
            IsCaseSensitive = Settings.Default.CaseSensitive;
            DoNotSearchSubfolders = Settings.Default.NoSubdirs;

            SearchResults = new();
            HaltRequest = new();

            MatchType = 0;

            MatchToolTip = $"Type of match to use:" + Environment.NewLine +
                            $"Simple: search for the text in the name as a substring." + Environment.NewLine +
                            $"Wildcards: use wildcards (?, *)" + Environment.NewLine +
                            $"RegEx: use a regualar expression (case sensitive)";

            SearchResultTooltip = "Results of the search. You can double-click on an " + Environment.NewLine +
                                  "entry to open an Explorer window with the file/directory selected.";

            SearchTooltip = "Select the root path where to start the search from" + Environment.NewLine +
                            "and enter the search term. Simple, wildcard, and " + Environment.NewLine +
                            "regular expression matching are available.";

            StartCmd = new( Start );
            StopCmd = new( Stop );
            OpenFolderCmd = new( OpenFolder );
            DoubleClickCmd = new( HandleDoubleClick );

            IsRunning = false;

            Application.Current.MainWindow.Closing += new CancelEventHandler( MainWindow_Closing );
        }


        void MainWindow_Closing( object? sender, CancelEventArgs? e )
        {
            if (IsRunning)
            {
                MessageBox.Show( "Halt the search before closing the window!" );
                e.Cancel = true;
            }
        }

        private void SaveParameters( )
        {
            Settings.Default.SearchPath = StartPath;
            Settings.Default.SearchString = SearchString;
            Settings.Default.MatchType = MatchType;
            Settings.Default.CaseSensitive = IsCaseSensitive;
            Settings.Default.NoSubdirs = DoNotSearchSubfolders;
            Settings.Default.Save();
        }

        private void Start()
        {
            if ( string.IsNullOrWhiteSpace( SearchString ) ||
                string.IsNullOrWhiteSpace( StartPath ) ||
                !Directory.Exists( StartPath ) )
            {
                MessageBox.Show( "Wrong parameters" );
                return;
            }

            IsRunning = true;

            SaveParameters();

            SearchResults.Clear();

            HaltRequest = new CancellationTokenSource();

            Thread t = new Thread( () => WorkingThread( new SearchOptionsContainer()
            {
                NoSubdirectories = DoNotSearchSubfolders,
                RootPath = StartPath,
                SearchString = SearchString,
                MatchType = MatchType,
                CaseSensitive = IsCaseSensitive
            },
            HaltRequest.Token ) );

            t.Start();
        }

        private void Stop()
        {
            HaltRequest?.Cancel();
        }

        private void OpenFolder()
        {
            OpenFolderDialog ofd = new();

            if ( ofd.ShowDialog() is true )
            {
                StartPath = ofd.FolderName;
            }
        }

        private void HandleDoubleClick( object? item )
        {
            var i = item as SearchResultEntry;

            if ( i is not null && !string.IsNullOrWhiteSpace( i.Path ) )
            {
                try
                {
                    Process.Start( "explorer.exe", string.Format( "/select,\"{0}\"", i.FullPath ) );

                }
                catch ( Exception ) 
                {
                    MessageBox.Show( "Error while starting an Explorer process for the file/folder:" + Environment.NewLine + i.FullPath );
                }
            }
        }

        delegate IEnumerable<FileInfo>? FileSearch( DirectoryInfo dir, string search );
        delegate IEnumerable<DirectoryInfo>? DirectorySearch( DirectoryInfo dir, string search );

        private void WorkingThread( SearchOptionsContainer options, CancellationToken pleaseHalt )
        {
            FileSearch fileSearchFunc = ( x, y ) => null;
            DirectorySearch directorySearchFunc = ( x, y ) => null;

            Stack<string> PathToExplore = new();

            PathToExplore.Push( options.RootPath );

            int easyCounter = 0;

            switch ( options.MatchType )
            {
                case 0:
                    {
                        fileSearchFunc = ( d, s ) => d.EnumerateFiles().Where( x => x.Name.Contains( options.SearchString, options.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase ) );
                        directorySearchFunc = ( d, s ) => d.EnumerateDirectories().Where( x => x.Name.Contains( options.SearchString, options.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase ) );
                    }
                    break;

                case 1:
                    {
                        fileSearchFunc = ( d, s ) => d.EnumerateFiles( s, new EnumerationOptions() { IgnoreInaccessible = true, MatchCasing = options.CaseSensitive ? MatchCasing.CaseSensitive : MatchCasing.CaseInsensitive } );
                        directorySearchFunc = ( d, s ) => d.EnumerateDirectories( s, new EnumerationOptions() { IgnoreInaccessible = true, MatchCasing = options.CaseSensitive ? MatchCasing.CaseSensitive : MatchCasing.CaseInsensitive } );
                    }
                    break;

                case 2:
                    {
                        fileSearchFunc = ( d, s ) =>
                        {
                            return d.EnumerateFiles( "*", new EnumerationOptions() { IgnoreInaccessible = true } ).Where( x => Regex.IsMatch( x.Name, options.SearchString, options.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase ) );
                        };

                        directorySearchFunc = ( d, s ) =>
                        {
                            return d.EnumerateDirectories( "*", new EnumerationOptions() { IgnoreInaccessible = true } ).Where( x => Regex.IsMatch( x.Name, options.SearchString, options.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase ) );
                        };
                    }
                    break;
            }

            while ( PathToExplore.Count > 0 && !pleaseHalt.IsCancellationRequested )
            {
                string path = PathToExplore.Pop();

                if ( !Directory.Exists( path ) )
                {
                    continue;
                }

                if ( (easyCounter & 32) != 0 )
                {
                    StatusLeft = $"Folders remaining: {PathToExplore.Count}";
                    StatusCenter = $"Current folder: {path}";
                }

                DirectoryInfo currentDir = new( path );

                if ( !options.NoSubdirectories )
                {
                    try
                    {
                        var newDirs = currentDir.EnumerateDirectories( "*", new EnumerationOptions() { IgnoreInaccessible = true } );

                        foreach ( var d in newDirs )
                        {
                            PathToExplore.Push( d.FullName );
                        }
                    }
                    catch ( Exception ex )
                    {
                    }
                }

                IEnumerable<DirectoryInfo>? foundDirs;

                try
                {
                    foundDirs = directorySearchFunc( currentDir, options.SearchString );
                }
                catch ( Exception ex )
                {
                    foundDirs = null;
                }

                if ( foundDirs is not null )
                {
                    foreach ( var d in foundDirs )
                    {
                        Application.Current.Dispatcher.Invoke( () =>
                        {
                            SearchResults.Add( new SearchResultEntry()
                            {
                                FullName = d.Name,
                                Extension = string.Empty,
                                Path = currentDir.FullName,
                                Size = string.Empty,
                                FullPath = d.FullName,
                            } );
                        } );
                    }
                }

                IEnumerable<FileInfo>? foundFiles;

                try
                {
                    foundFiles = fileSearchFunc( currentDir, options.SearchString );
                }
                catch ( Exception ex )
                {
                    foundFiles = null;
                }

                if ( foundFiles is not null )
                {
                    foreach ( var f in foundFiles )
                    {
                        Application.Current.Dispatcher.Invoke( () =>
                        {
                            SearchResults.Add( new SearchResultEntry()
                            {
                                FullName = f.Name,
                                Extension = f.Extension,
                                Path = currentDir.FullName,
                                Size = f.Length.ToString(),
                                FullPath = f.FullName,
                            } );
                        } );
                    }
                }
                easyCounter++;
            }

            StatusLeft = "Folders remaining: 0";
            StatusCenter = string.Empty;

            IsRunning = false;
        }
    }
}
