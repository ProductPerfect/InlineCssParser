﻿using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using EnvDTE;
using System.Linq;
using System.Collections.Generic;

namespace InlineCssParser
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class Command
    {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0100;
        private Parser _parser;
        private IVsStatusbar bar;

        private IVsStatusbar StatusBar
        {
            get
            {
                if (bar == null)
                {
                    bar = this.ServiceProvider.GetService(typeof(SVsStatusbar)) as IVsStatusbar;
                }

                return bar;
            }
        }

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("82b0ea61-76c4-4c2c-bbf1-03ec5f8523c3");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly Package _package;

        /// <summary>
        /// Initializes a new instance of the <see cref="Command"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        private Command(Package package, OleMenuCommandService commandService)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            _package = package ?? throw new ArgumentNullException(nameof(package));

            _parser = new Parser();
       
            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.MenuItemCallback, menuCommandID);
            commandService.AddCommand(menuItem);           
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static Command Instance
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private IServiceProvider ServiceProvider
        {
            get
            {
                return _package;
            }
        }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new Command(package, commandService);
        }

        /// <summary>
        /// This function is the callback used to execute the command when the menu item is clicked.
        /// See the constructor to see how the menu item is associated with this function using
        /// OleMenuCommandService service and MenuCommand class.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private void MenuItemCallback(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            DTE dte = Package.GetGlobalService(typeof(DTE)) as DTE;
            Document doc = dte.ActiveDocument;
            TextDocument txtDoc = doc.Object() as TextDocument;

            var text = txtDoc.CreateEditPoint(txtDoc.StartPoint).GetText(txtDoc.EndPoint);
            text = text.Replace("\r", "");

            uint cookie = 0;
            StatusBar.Progress(ref cookie, 1, string.Empty, 0, 0);

            if (txtDoc.Language == "HTMLX" || txtDoc.Language == "HTML")
            {
                var html = text;
                var elementList = new List<HtmlElement>();
                var parsed = _parser.ParseHtml(html, elementList, txtDoc, StatusBar, ref cookie);
                var cssFileContent = string.Empty;

                if (elementList.Any())
                {
                    foreach (var item in elementList)
                    {
                        var cssClass = string.Empty;
                        if (string.IsNullOrEmpty(item.Class))
                        {
                            cssClass = string.Format(".{0}", string.IsNullOrEmpty(item.Id) ? CreateUniqueElementKey(item.Name, item.LineNumber) : item.Id);
                        }
                        else
                        {
                            cssClass = string.Format(".{0} .{1}", item.Class, CreateUniqueElementKey(item.Name, item.LineNumber));
                        }

                        var idAttr = string.IsNullOrEmpty(item.Id) ? string.Empty : string.Format("id=\"{0}\"", item.Id);
                        var replaceText = string.Format("{0} {1} class=\"{2}\"", item.Name, idAttr, cssClass.Replace(".", string.Empty));

                        parsed = parsed.Replace(item.Guid, replaceText);
                        cssFileContent += string.Format("{0}{{{1}}}\n\n", cssClass, "\n" + item.Style);
                    }

                    //css file beautification
                    cssFileContent = cssFileContent.Replace(";", ";\n");

                    //update html file
                    var txtSelHtml = (TextSelection)doc.Selection;
                    txtSelHtml.SelectAll();
                    txtSelHtml.Delete();
                    txtSelHtml.Insert(parsed);

                    //create css file
                    var docName = doc.Name.Substring(0, doc.Name.IndexOf('.'));
                    docName = string.Format("{0}.css", docName);
                  
                    dte.ItemOperations.NewFile(@"General\Text File", docName, EnvDTE.Constants.vsViewKindTextView);
               
                    var txtSelCss = (TextSelection)dte.ActiveDocument.Selection;
                    txtSelCss.SelectAll();
                    txtSelCss.Delete();
                    txtSelCss.Insert(cssFileContent);
                }
                else
                {
                    VsShellUtilities.ShowMessageBox(this.ServiceProvider, "Not found inline css.", "That's Cool!",
                        OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                }
            }
            else
            {
                VsShellUtilities.ShowMessageBox(this.ServiceProvider, "This is not a html file!", "Oops!",
                    OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }

            // Clear the progress bar.
            StatusBar.Progress(ref cookie, 0, string.Empty, 0, 0);
            StatusBar.FreezeOutput(0);
            StatusBar.Clear();
        }

        private string CreateUniqueElementKey(string name, int lineNumber)
        {
            Random r = new Random();
            var randomNumber = r.Next(1000, 9999);
            //now with the random number this should be fully unique across all other files...
            return string.Format("{0}_line_{1}_random_{2}", name, lineNumber, randomNumber.ToString());
        }
    }
}