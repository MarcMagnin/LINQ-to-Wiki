﻿using System;
using System.Collections.Generic;
using System.Linq;
using LinqToWiki.Codegen.ModuleInfo;
using LinqToWiki.Collections;
using LinqToWiki.Parameters;
using Roslyn.Compilers.CSharp;

namespace LinqToWiki.Codegen.ModuleGenerators
{
    class SingleQueryModuleGenerator : ModuleGenerator
    {
        public SingleQueryModuleGenerator(Wiki wiki)
            : base(wiki)
        {}

        protected override void GenerateMethod(Module module)
        {
            GenerateMethod(
                module, module.Parameters.Where(p => p.Name != "prop"), ResultClassName, null, Wiki.Names.QueryAction,
                true, null);
        }

        protected override IEnumerable<Tuple<string, string>> GetBaseParameters(Module module)
        {
            return Wiki.QueryBaseParameters.Concat(
                new TupleList<string, string>
                {
                    { module.QueryType.ToString().ToLowerInvariant(), module.Name },
                    {
                        module.Prefix + "prop",
                        NameValueParameter.JoinValues(
                            module.PropertyGroups.Select(g => g.Name).Where(n => n != string.Empty))
                    }
                });
        }

        protected override ClassDeclarationSyntax GenerateResultClass(IEnumerable<PropertyGroup> propertyGroups)
        {
            var resultClass = GenerateClassForProperties(ResultClassName, propertyGroups.SelectMany(g => g.Properties));

            var parseMethodBody = resultClass.DescendentNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(m => m.Identifier.ValueText == "Parse")
                .BodyOpt;

            var statements = parseMethodBody.Statements;

            var newStatement = SyntaxEx.Assignment(
                Syntax.IdentifierName("element"),
                SyntaxEx.Invocation(
                    SyntaxEx.MemberAccess(SyntaxEx.Invocation(SyntaxEx.MemberAccess("element", "Elements")), "Single")));

            var newStatements = new[] { newStatement }.Concat(statements);

            var newBody = SyntaxEx.Block(newStatements);

            return resultClass.ReplaceNode(parseMethodBody, newBody);
        }
    }
}