﻿using System.Collections.Generic;
using System.Linq;
using Rubberduck.Interaction;
using Rubberduck.Parsing;
using Rubberduck.Parsing.Grammar;
using Rubberduck.Parsing.Rewriter;
using Rubberduck.Parsing.Symbols;
using Rubberduck.Parsing.VBA;
using Rubberduck.Refactorings.Exceptions;
using Rubberduck.Refactorings.Exceptions.IntroduceParameter;
using Rubberduck.Resources;
using Rubberduck.VBEditor;
using Rubberduck.VBEditor.Utility;

namespace Rubberduck.Refactorings.IntroduceParameter
{
    public class IntroduceParameterRefactoring : RefactoringBase
    {
        private readonly IBaseRefactoring<IntroduceParameterModel> _baseRefactoring;
        private readonly ISelectedDeclarationProvider _selectedDeclarationProvider;
        private readonly IMessageBox _messageBox;

        public IntroduceParameterRefactoring(
            IntroduceParameterBaseRefactoring baseRefactoring, 
            IMessageBox messageBox, 
            IRewritingManager rewritingManager,
            ISelectionProvider selectionProvider,
            ISelectedDeclarationProvider selectedDeclarationProvider)
        :base(rewritingManager, selectionProvider)
        {
            _baseRefactoring = baseRefactoring;
            _selectedDeclarationProvider = selectedDeclarationProvider;
            _messageBox = messageBox;
        }

        protected override Declaration FindTargetDeclaration(QualifiedSelection targetSelection)
        {
            var selectedDeclaration = _selectedDeclarationProvider.SelectedDeclaration(targetSelection);
            if (selectedDeclaration == null
                || selectedDeclaration.DeclarationType != DeclarationType.Variable)
            {
                return null;
            }

            return selectedDeclaration;
        }

        public override void Refactor(Declaration target)
        {
            if (target == null)
            {
                throw new TargetDeclarationIsNullException();
            }

            if (target.DeclarationType != DeclarationType.Variable)
            {
                throw new InvalidDeclarationTypeException(target);
            }

            if (!target.ParentScopeDeclaration.DeclarationType.HasFlag(DeclarationType.Member))
            {
                throw new TargetDeclarationIsNotContainedInAMethodException(target);
            }

            PromoteVariable(target);
        }

        private void PromoteVariable(Declaration target)
        {
            if (!PromptIfMethodImplementsInterface(target))
            {
                return;
            }

            var model = Model(target);
            _baseRefactoring.Refactor(model);
        }

        private IntroduceParameterModel Model(Declaration target)
        {
            var enclosingMember = _selectedDeclarationProvider.SelectedMember(target.QualifiedSelection);
            return new IntroduceParameterModel(target, enclosingMember);
        }

        private bool PromptIfMethodImplementsInterface(Declaration targetVariable)
        {
            var functionDeclaration = _selectedDeclarationProvider.SelectedMember(targetVariable.QualifiedSelection);

            if (functionDeclaration == null || !functionDeclaration.IsInterfaceImplementation)
            {
                return true;
            }

            var interfaceImplementation = functionDeclaration.InterfaceMemberImplemented;

            if (interfaceImplementation == null)
            {
                return true;
            }

            var message = string.Format(RubberduckUI.IntroduceParameter_PromptIfTargetIsInterface,
                functionDeclaration.IdentifierName, interfaceImplementation.ComponentName,
                interfaceImplementation.IdentifierName);

            return _messageBox.Question(message, RubberduckUI.IntroduceParameter_Caption);
        }
    }
}
