﻿using System;
using System.Linq;

namespace TextrudeInteractive
{
    /// <summary>
    ///     Holds all the input necessary to run a template rendering pass
    /// </summary>
    /// <remarks> </remarks>
    public record EngineInputSet
    {
        public static EngineInputSet EmptyYaml =
            new(string.Empty, Array.Empty<ModelText>(), string.Empty, string.Empty);

        public EngineInputSet(string template, ModelText[] models, string definitionsText,
            string includes)
        {
            Template = template;
            Models = models;

            Definitions = definitionsText
                .Split('\r', '\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToArray();

            IncludePaths = includes.Split(Environment.NewLine,
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        }

        //Required for deserialisation
        public EngineInputSet()
        {
        }

        /// <summary>
        /// </summary>
        public string[] Definitions { get; init; } = Array.Empty<string>();

        public string[] IncludePaths { get; init; } = Array.Empty<string>();

        public ModelText[] Models { get; init; } = Array.Empty<ModelText>();

        public string Template { get; init; } = string.Empty;
    }
}