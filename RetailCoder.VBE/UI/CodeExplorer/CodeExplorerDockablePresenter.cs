﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Vbe.Interop;
using Rubberduck.Common;
using Rubberduck.Parsing;
using Rubberduck.Parsing.Grammar;
using Rubberduck.Parsing.Symbols;
using Rubberduck.Parsing.VBA;
using Rubberduck.UnitTesting;
using Rubberduck.VBEditor.Extensions;
using Rubberduck.VBEditor.VBEInterfaces.RubberduckCodePane;

namespace Rubberduck.UI.CodeExplorer
{
    public class CodeExplorerDockablePresenter : DockableToolwindowPresenter
    {
        private readonly IRubberduckParser _parser;
        private readonly ICodePaneWrapperFactory _wrapperFactory;
        private CodeExplorerWindow Control { get { return UserControl as CodeExplorerWindow; } }

        public CodeExplorerDockablePresenter(IRubberduckParser parser, VBE vbe, AddIn addIn, CodeExplorerWindow view, ICodePaneWrapperFactory wrapperFactory)
            : base(vbe, addIn, view)
        {
            _parser = parser;
            //_parser.ParseStarted += _parser_ParseStarted;
            //_parser.ParseCompleted += _parser_ParseCompleted;
            RegisterControlEvents();
            _wrapperFactory = wrapperFactory;
        }

        public override void Show()
        {
            base.Show();
            Task.Run(() => RefreshExplorerTreeView()); 
        }

        private void RegisterControlEvents()
        {
            if (Control == null)
            {
                return;
            }

            Control.RefreshTreeView += RefreshExplorerTreeView;
            Control.NavigateTreeNode += NavigateExplorerTreeNode;
            Control.AddComponent += AddComponent;
            Control.AddTestModule += AddTestModule;
            Control.ToggleFolders += ToggleFolders;
            Control.ShowDesigner += ShowDesigner;
            Control.DisplayStyleChanged += DisplayStyleChanged;
            Control.RunAllTests += ContextMenuRunAllTests;
            Control.RunInspections += ContextMenuRunInspections;
            Control.SelectionChanged += SelectionChanged;
            Control.Rename += RenameSelection;
            Control.FindAllReferences += FindAllReferencesForSelection;
            Control.FindAllImplementations += FindAllImplementationsForSelection;
        }

        public event EventHandler<NavigateCodeEventArgs> FindAllReferences;
        private void FindAllReferencesForSelection(object sender, NavigateCodeEventArgs e)
        {
            var handler = FindAllReferences;
            if (handler != null)
            {
                handler(sender, e);
            }
        }

        public event EventHandler<NavigateCodeEventArgs> FindAllImplementations;
        private void FindAllImplementationsForSelection(object sender, NavigateCodeEventArgs e)
        {
            var handler = FindAllImplementations;
            if (handler != null)
            {
                handler(sender, e);
            }
        }

        public event EventHandler<TreeNodeNavigateCodeEventArgs> Rename;
        private void RenameSelection(object sender, TreeNodeNavigateCodeEventArgs e)
        {
            if (e.Node == null || e.Node.Tag == null)
            {
                return;
            }

            var handler = Rename;
            if (handler != null)
            {
                handler(this, e);
                RefreshExplorerTreeView();
                e.Node.EnsureVisible();
            }
        }

        private void SelectionChanged(object sender, TreeNodeNavigateCodeEventArgs e)
        {
            if (e.Node == null || e.Node.Tag == null)
            {
                return;
            }

            try
            {
                VBE.ActiveVBProject = e.QualifiedName.Project;
            }
            catch (COMException)
            {
                // swallow "catastrophic failure"
            }
        }

        public event EventHandler RunInspections;
        private void ContextMenuRunInspections(object sender, EventArgs e)
        {
            var handler = RunInspections;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        public event EventHandler RunAllTests;
        private void ContextMenuRunAllTests(object sender, EventArgs e)
        {
            var handler = RunAllTests;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void DisplayStyleChanged(object sender, EventArgs e)
        {
            RefreshExplorerTreeView();
        }

        private void AddTestModule(object sender, EventArgs e)
        {
            NewUnitTestModuleCommand.NewUnitTestModule(VBE);
            RefreshExplorerTreeView();
        }

        private void ShowDesigner(object sender, EventArgs e)
        {
            var node = Control.SolutionTree.SelectedNode;
            if (node != null && node.Tag != null)
            {
                var selection = (Declaration)node.Tag;
                var module = selection.QualifiedName.QualifiedModuleName.Component.CodeModule;
                if (module == null)
                {
                    return;
                }

                try
                {
                    module.Parent.DesignerWindow().Visible = true;
                }
                catch
                {
                    Control.ShowDesignerButton.Enabled = false;
                }
            }
        }

        private bool _showFolders = true;
        private void ToggleFolders(object sender, EventArgs e)
        {
            _showFolders = !_showFolders;
            RefreshExplorerTreeView();
        }

        private void AddComponent(object sender, AddComponentEventArgs e)
        {
            var project = VBE.ActiveVBProject;
            if (project == null)
            {
                return;
            }

            project.VBComponents.Add(e.ComponentType);
            RefreshExplorerTreeView();
        }

        private void NavigateExplorerTreeNode(object sender, TreeNodeNavigateCodeEventArgs e)
        {
            var declaration = e.Declaration;
            if (declaration != null)
            {
                VBE.SetSelection(declaration.QualifiedName.QualifiedModuleName.Project, declaration.Selection, declaration.QualifiedName.QualifiedModuleName.Component.Name, _wrapperFactory);
            }
        }

        private void RefreshExplorerTreeView(object sender, EventArgs e)
        {
            Task.Run(() => RefreshExplorerTreeView());
        }

        private void RefreshExplorerTreeView()
        {
            //Control.Invoke((MethodInvoker) delegate
            //{
            //    Control.SolutionTree.Nodes.Clear();
            //    Control.ShowDesignerButton.Enabled = false;
            //});

            //_parser.Parse(VBE, this);
        }

        private void AddProjectNodes(IRubberduckParserState parseResult, TreeNode root)
        {
            var projects = parseResult.AllDeclarations.OfType(DeclarationType.Project);
            foreach (var project in projects)
            {
                if (project.Project.Protection == vbext_ProjectProtection.vbext_pp_locked)
                {
                    root.ImageKey = "Locked";
                }
                else
                {
                    var nodes = CreateModuleNodes(parseResult);
                    AddProjectFolders(project.Project, root, nodes.ToArray());
                    root.ImageKey = "ClosedFolder";
                    root.Expand();
                }

                root.Tag = project;
                root.Text = project.ProjectName;
            }
        }

        private static readonly IDictionary<vbext_ComponentType, string> ComponentTypeIcons =
            new Dictionary<vbext_ComponentType, string>
            {
                { vbext_ComponentType.vbext_ct_StdModule, "StandardModule"},
                { vbext_ComponentType.vbext_ct_ClassModule, "ClassModule"},
                { vbext_ComponentType.vbext_ct_Document, "OfficeDocument"},
                { vbext_ComponentType.vbext_ct_ActiveXDesigner, "ClassModule"},
                { vbext_ComponentType.vbext_ct_MSForm, "Form"}
            };

        private void AddProjectFolders(VBProject project, TreeNode root, TreeNode[] components)
        {
            var documentNodes = components.Where(node => node.ImageKey == "OfficeDocument")
                                          .OrderBy(node => node.Text)
                                          .ToArray();
            if (project.VBComponents.Cast<VBComponent>()
                       .Any(component => component.Type == vbext_ComponentType.vbext_ct_Document))
            {
                AddFolderNode(root, "Documents", "ClosedFolder", documentNodes);
            }

            var formsNodes = components.Where(node => node.ImageKey == "Form")
                                       .OrderBy(node => node.Text)
                                       .ToArray();
            if (project.VBComponents.Cast<VBComponent>()
                       .Any(component => component.Type == vbext_ComponentType.vbext_ct_MSForm))
            {
                AddFolderNode(root, "Forms", "ClosedFolder", formsNodes);
            }

            var stdModulesNodes = components.Where(node => node.ImageKey == "StandardModule")
                                            .OrderBy(node => node.Text)
                                            .ToArray();
            if (project.VBComponents.Cast<VBComponent>()
                       .Any(component => component.Type == vbext_ComponentType.vbext_ct_StdModule))
            {
                AddFolderNode(root, "Standard Modules", "ClosedFolder", stdModulesNodes);
            }

            var classModulesNodes = components.Where(node => node.ImageKey == "ClassModule")
                                              .OrderBy(node => node.Text)
                                              .ToArray();
            if (project.VBComponents.Cast<VBComponent>()
                       .Any(component => component.Type == vbext_ComponentType.vbext_ct_ClassModule
                                      || component.Type == vbext_ComponentType.vbext_ct_ActiveXDesigner))
            {
                AddFolderNode(root, "Class Modules", "ClosedFolder", classModulesNodes);
            }
        }

        private void AddFolderNode(TreeNode root, string text, string imageKey, TreeNode[] nodes)
        {
            if (_showFolders)
            {
                var node = root.Nodes.Add(text);
                node.ImageKey = imageKey;
                node.SelectedImageKey = imageKey;
                node.Nodes.AddRange(nodes);
                node.Expand();
            }
            else
            {
                root.Nodes.AddRange(nodes);
            }
        }

        private IEnumerable<TreeNode> CreateModuleNodes(IRubberduckParserState parseResult)
        {
            var declarations = parseResult.AllDeclarations.ToList();
            var result = new List<TreeNode>();
            foreach (var componentParseResult in declarations.GroupBy(d => d.QualifiedName.QualifiedModuleName))
            {
                var component = componentParseResult.Key.Component;
                var members = declarations
                    .Where(declaration => declaration.ParentScope == component.Collection.Parent.Name + "." + component.Name
                        && declaration.DeclarationType != DeclarationType.Control
                        && declaration.DeclarationType != DeclarationType.ModuleOption);

                var node = new TreeNode(component.Name);
                node.ImageKey = ComponentTypeIcons[component.Type];
                node.SelectedImageKey = node.ImageKey;
                node.Tag = declarations.SingleOrDefault(item => 
                    item.IdentifierName == component.Name 
                    && item.Project == component.Collection.Parent
                    && (item.DeclarationType == DeclarationType.Class || item.DeclarationType == DeclarationType.Module));

                foreach (var declaration in members)
                {
                    if (declaration.DeclarationType == DeclarationType.UserDefinedTypeMember
                        || declaration.DeclarationType == DeclarationType.EnumerationMember)
                    {
                        // these ones are handled by their respective parent
                        continue;
                    }

                    var text = GetNodeText(declaration);
                    var child = new TreeNode(text);
                    child.ImageKey = GetImageKeyForDeclaration(declaration);
                    child.SelectedImageKey = child.ImageKey;
                    child.Tag = declaration;

                    if (declaration.DeclarationType == DeclarationType.UserDefinedType
                        || declaration.DeclarationType == DeclarationType.Enumeration)
                    {
                        var subDeclaration = declaration;
                        var subMembers = declarations.Where(item => 
                            (item.DeclarationType == DeclarationType.EnumerationMember || item.DeclarationType == DeclarationType.UserDefinedTypeMember)
                            && item.Context != null && subDeclaration.Context.Equals(item.Context.Parent));

                        foreach (var subMember in subMembers)
                        {
                            var subChild = new TreeNode(subMember.IdentifierName);
                            subChild.ImageKey = GetImageKeyForDeclaration(subMember);
                            subChild.SelectedImageKey = subChild.ImageKey;
                            subChild.Tag = subMember;
                            child.Nodes.Add(subChild);
                        }
                    }

                    node.Nodes.Add(child);
                }

                result.Add(node);
            }

            return result;
        }

        private string GetNodeText(Declaration declaration)
        {
            if (Control.DisplayStyle == TreeViewDisplayStyle.MemberNames)
            {
                var result = declaration.IdentifierName;
                if (declaration.DeclarationType == DeclarationType.PropertyGet)
                {
                    result += " (" + Tokens.Get + ")";
                }
                else if (declaration.DeclarationType == DeclarationType.PropertyLet)
                {
                    result += " (" + Tokens.Let + ")";
                }
                else if (declaration.DeclarationType == DeclarationType.PropertySet)
                {
                    result += " (" + Tokens.Set + ")";
                }

                return result;
            }

            if (declaration.DeclarationType == DeclarationType.Procedure)
            {
                return ((VBAParser.SubStmtContext) declaration.Context).Signature();
            }

            if (declaration.DeclarationType == DeclarationType.Function)
            {
                return ((VBAParser.FunctionStmtContext)declaration.Context).Signature();
            }

            if (declaration.DeclarationType == DeclarationType.PropertyGet)
            {
                return ((VBAParser.PropertyGetStmtContext)declaration.Context).Signature();
            }

            if (declaration.DeclarationType == DeclarationType.PropertyLet)
            {
                return ((VBAParser.PropertyLetStmtContext)declaration.Context).Signature();
            }

            if (declaration.DeclarationType == DeclarationType.PropertySet)
            {
                return ((VBAParser.PropertySetStmtContext)declaration.Context).Signature();
            }

            return declaration.IdentifierName;
        }

        private string GetImageKeyForDeclaration(Declaration declaration)
        {
            var result = string.Empty;
            switch (declaration.DeclarationType)
            {
                case DeclarationType.Module:
                    break;
                case DeclarationType.Class:
                    break;
                case DeclarationType.Procedure:
                case DeclarationType.Function:
                    if (declaration.Accessibility == Accessibility.Private)
                    {
                        result = "PrivateMethod";
                        break;
                    }
                    if (declaration.Accessibility == Accessibility.Friend)
                    {
                        result = "FriendMethod";
                        break;
                    }
                    result = "PublicMethod";
                    break;

                case DeclarationType.PropertyGet:
                case DeclarationType.PropertyLet:
                case DeclarationType.PropertySet:
                    if (declaration.Accessibility == Accessibility.Private)
                    {
                        result = "PrivateProperty";
                        break;
                    }
                    if (declaration.Accessibility == Accessibility.Friend)
                    {
                        result = "FriendProperty";
                        break;
                    }
                    result = "PublicProperty";
                    break;

                case DeclarationType.Parameter:
                    break;
                case DeclarationType.Variable:
                    if (declaration.Accessibility == Accessibility.Private)
                    {
                        result = "PrivateField";
                        break;
                    }
                    if (declaration.Accessibility == Accessibility.Friend)
                    {
                        result = "FriendField";
                        break;
                    }
                    result = "PublicField";
                    break;

                case DeclarationType.Constant:
                    if (declaration.Accessibility == Accessibility.Private)
                    {
                        result = "PrivateConst";
                        break;
                    }
                    if (declaration.Accessibility == Accessibility.Friend)
                    {
                        result = "FriendConst";
                        break;
                    }
                    result = "PublicConst";
                    break;

                case DeclarationType.Enumeration:
                    if (declaration.Accessibility == Accessibility.Private)
                    {
                        result = "PrivateEnum";
                        break;
                    }
                    if (declaration.Accessibility == Accessibility.Friend)
                    {
                        result = "FriendEnum";
                        break;
                    }
                    result = "PublicEnum";
                    break;

                case DeclarationType.EnumerationMember:
                    result = "EnumItem";
                    break;

                case DeclarationType.Event:
                    if (declaration.Accessibility == Accessibility.Private)
                    {
                        result = "PrivateEvent";
                        break;
                    }
                    if (declaration.Accessibility == Accessibility.Friend)
                    {
                        result = "FriendEvent";
                        break;
                    }
                    result = "PublicEvent";
                    break;

                case DeclarationType.UserDefinedType:
                    if (declaration.Accessibility == Accessibility.Private)
                    {
                        result = "PrivateType";
                        break;
                    }
                    if (declaration.Accessibility == Accessibility.Friend)
                    {
                        result = "FriendType";
                        break;
                    }
                    result = "PublicType";
                    break;

                case DeclarationType.UserDefinedTypeMember:
                    result = "PublicField";
                    break;
                
                case DeclarationType.LibraryProcedure:
                case DeclarationType.LibraryFunction:
                    result = "Identifier";
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }

            return result;
        }
    }
}
