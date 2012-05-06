﻿using System;
using System.Collections.Generic;
using System.Linq;
using LinqToWiki.Codegen.ModuleInfo;
using LinqToWiki.Collections;
using Roslyn.Compilers.CSharp;

namespace LinqToWiki.Codegen
{
    class TypeManager
    {
        private readonly Wiki m_wiki;

        private readonly DictionaryDictionary<string, EnumParameterType, string> m_enumTypeNames =
            new DictionaryDictionary<string, EnumParameterType, string>();

        public TypeManager(Wiki wiki)
        {
            m_wiki = wiki;
        }

        public string GetTypeName(
            Parameter parameter, string moduleName, bool nullable, bool useItemOrCollection = true)
        {
            return GetTypeName(parameter.Type, parameter.Name, moduleName, parameter.Multi, nullable, useItemOrCollection);
        }

        public string GetTypeName(
            ParameterType parameterType, string propertyName, string moduleName, bool multi, bool nullable = false,
            bool useItemOrCollection = true)
        {
            var simpleType = parameterType as SimpleParameterType;
            if (simpleType != null)
                return GetSimpleTypeName(simpleType, propertyName, multi, nullable, useItemOrCollection);

            return GetEnumTypeName(
                (EnumParameterType)parameterType, propertyName, moduleName, multi, useItemOrCollection);
        }

        private static string GetSimpleTypeName(
            SimpleParameterType simpleType, string propertyName, bool multi, bool nullable, bool useItemOrCollection)
        {
            string result;

            switch (simpleType.Name)
            {
            case "string":
            case "user":
                result = "string";
                break;
            case "timestamp":
                result = "DateTime";
                break;
            case "namespace":
                result = "Namespace";
                break;
            case "boolean":
                result = "bool";
                break;
            case "integer":
                result = propertyName.EndsWith("id") ? "long" : "int";
                break;
            default:
                throw new InvalidOperationException(string.Format("Unknown type {0}", simpleType.Name));
            }

            if (multi)
                result = string.Format(useItemOrCollection ? "ItemOrCollection<{0}>" : "IEnumerable<{0}>", result);
            else if (nullable && simpleType.Name != "string" && simpleType.Name != "namespace")
                result += '?';

            return result;
        }

        private string GetEnumTypeName(
            EnumParameterType enumType, string propertyName, string moduleName, bool multi, bool useItemOrCollection)
        {
            string result;
            if (!m_enumTypeNames.TryGetValue(moduleName, enumType, out result))
                result = GenerateType(enumType, propertyName, moduleName);

            if (multi)
                result = string.Format(useItemOrCollection ? "ItemOrCollection<{0}>" : "IEnumerable<{0}>", result);

            return result;
        }

        private string GenerateType(EnumParameterType enumType, string propertyName, string moduleName)
        {
            string typeName = moduleName + propertyName;

            Dictionary<EnumParameterType, string> moduleTypes;

            if (m_enumTypeNames.TryGetValue(moduleName, out moduleTypes))
            {
                int i = 2;
                while (moduleTypes.Values.Contains(typeName))
                    typeName = moduleName + propertyName + i++;
            }

            var fixedMemberNameMapping = new TupleList<string, string>();
            var memberNames = new List<string>();

            foreach (var name in enumType.Values)
            {
                var fixedName = FixEnumMemberName(name);

                if (name != fixedName.TrimStart('@'))
                    fixedMemberNameMapping.Add(fixedName, name);

                memberNames.Add(fixedName);
            }

            var members = enumType.Values.Select(
                memberName =>
                SyntaxEx.FieldDeclaration(
                    new[] { SyntaxKind.PublicKeyword, SyntaxKind.StaticKeyword, SyntaxKind.ReadOnlyKeyword },
                    typeName, FixEnumMemberName(memberName),
                    SyntaxEx.ObjectCreation(typeName, SyntaxEx.Literal(memberName))));

            var constructorParameter = SyntaxEx.Parameter("string", "value");
            var contructor = SyntaxEx.ConstructorDeclaration(
                new[] { SyntaxKind.InternalKeyword }, typeName, new[] { constructorParameter },
                constructorInitializer: SyntaxEx.BaseConstructorInitializer((NamedNode)constructorParameter));

            var classDeclaration =
                SyntaxEx.ClassDeclaration(typeName, SyntaxEx.ParseTypeName("StringValue"), contructor)
                    .WithAdditionalMembers(members);

            var namespaceDeclaration = m_wiki.Files[Wiki.Names.Enums].SingleDescendant<NamespaceDeclarationSyntax>();

            m_wiki.Files[Wiki.Names.Enums] = m_wiki.Files[Wiki.Names.Enums].ReplaceNode(
                namespaceDeclaration, namespaceDeclaration.WithAdditionalMembers(classDeclaration));

            m_enumTypeNames.Add(moduleName, enumType, typeName);

            return typeName;
        }

        private static readonly char[] ToReplace = "-/ ".ToCharArray();

        private static readonly string[] Restricted = new[] { "new", "true", "false" };

        private static string FixEnumMemberName(string value)
        {
            if (value == string.Empty)
                return "none";
            if (Restricted.Contains(value))
                return '@' + value;

            if (value[0] == '!')
                value = "not-" + value.Substring(1);

            foreach (var c in ToReplace)
                value = value.Replace(c, '_');

            return value;
        }

        // value is expected to be a string
        public ExpressionSyntax CreateConverter(Property property, string moduleName, ExpressionSyntax value, ExpressionSyntax wiki)
        {
            var simpleType = property.Type as SimpleParameterType;
            if (simpleType != null)
                return CreateSimpleConverter(simpleType, property.Name, value, wiki);

            return CreateEnumConverter((EnumParameterType)property.Type, moduleName, value);
        }

        private static ExpressionSyntax CreateSimpleConverter(
            SimpleParameterType simpleType, string propertyName, ExpressionSyntax value, ExpressionSyntax wiki)
        {
            if (simpleType.Name == "namespace")
                return SyntaxEx.Invocation(SyntaxEx.MemberAccess("ValueParser", "ParseNamespace"), value, wiki);

            string typeName;

            switch (simpleType.Name)
            {
            case "string":
            case "user":
                typeName = "String";
                break;
            case "timestamp":
                typeName = "DateTime";
                break;
            case "boolean":
                typeName = "Boolean";
                break;
            case "integer":
                typeName = propertyName.EndsWith("id") ? "Int64" : "Int32";
                break;
            default:
                throw new InvalidOperationException(string.Format("Unknown type {0}", simpleType.Name));
            }

            return SyntaxEx.Invocation(SyntaxEx.MemberAccess("ValueParser", "Parse" + typeName), value);
        }

        private ExpressionSyntax CreateEnumConverter(EnumParameterType type, string moduleName, ExpressionSyntax value)
        {
            var typeName = m_enumTypeNames[moduleName, type];
            return SyntaxEx.ObjectCreation(typeName, value);
        }
    }
}