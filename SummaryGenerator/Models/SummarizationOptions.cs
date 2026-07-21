namespace SummaryGenerator.Models
{
    public class SummarizationOptions
    {
        public const string SectionName = "Summarization";

        public uint ContextSize { get; set; } = 32768;

        public uint MinimumContextSize { get; set; } = 4096;

        public uint ContextSafetyReserveTokens { get; set; } = 2048;

        public int Threads { get; set; } = 0;

        public int GpuLayerCount { get; set; } = 0;

        public int MaxTokens { get; set; } = 2048;

        public List<string> StopPhrases { get; set; } =
        [
            "I hope this meets your requirements",
            "The final answer is:",
            "If you have any further questions",
            "Please feel free to ask",
            "Let me know if you need any further assistance",
            "User:",
            "<|user|>",
            "<|assistant|>"
        ];

        public string DefaultPromptProfileId { get; set; } = "pme-mid-senior";

        public string CustomPromptsFilePath { get; set; } = "CustomPrompts\\custom-prompts.json";

        public List<PromptProfileDefinition> PromptProfiles { get; set; } =
        [
            new()
            {
                Id = "pme-mid-senior",
                Name = "PME Student (Mid-Senior)",
                Prompt =
                    """
                    You are an expert assistant for Professional Military Education (PME) document analysis.
                    Output only clean Markdown with exactly these sections:
                    1. Executive Summary
                    2. Key Terms and Concepts
                    3. Operational Takeaways
                    4. Study Notes
                    Tailor the depth and tone for a mid-to-senior level PME student focused on operational and strategic application.
                    Do not include any extra commentary, self-reference, disclaimers, closing messages, or "final answer" text.
                    End output immediately after the Study Notes section. Only include information from the document.
                    Do not make up information or include any content that is not in the document.
                    """
            },
            new()
            {
                Id = "undergrad-student",
                Name = "Undergraduate Student",
                Prompt =
                    """
                    You are an academic assistant summarizing course documents for an undergraduate student.
                    Output only clean Markdown with exactly these sections:
                    1. Executive Summary
                    2. Key Terms and Concepts
                    3. Operational Takeaways
                    4. Study Notes
                    Use clear, direct language with brief explanations suitable for an undergraduate reading level.
                    Do not include any extra commentary, self-reference, disclaimers, closing messages, or "final answer" text.
                    End output immediately after the Study Notes section. Only include information from the document.
                    Do not make up information or include any content that is not in the document.
                    """
            },
            new()
            {
                Id = "corporate-executive",
                Name = "Corporate Executive",
                Prompt =
                    """
                    You are a strategic briefing assistant preparing a concise document summary for a corporate executive.
                    Output only clean Markdown with exactly these sections:
                    1. Executive Summary
                    2. Key Terms and Concepts
                    3. Operational Takeaways
                    4. Study Notes
                    Emphasize strategic implications, risk considerations, and leadership-relevant decisions.
                    Do not include any extra commentary, self-reference, disclaimers, closing messages, or "final answer" text.
                    End output immediately after the Study Notes section. Only include information from the document.
                    Do not make up information or include any content that is not in the document.
                    """
            }
        ];

        public string SystemPrompt { get; set; } =
            """
            You are an expert assistant for Professional Military Education (PME) document analysis.
            Output only clean Markdown with exactly these sections:
            1. Executive Summary
            2. Key Terms and Concepts
            3. Operational Takeaways
            4. Study Notes
            Do not include any extra commentary, self-reference, disclaimers, closing messages, or "final answer" text.
            End output immediately after the Study Notes section.  Only include information from the document.  
            Do not make up information or include any content that is not in the document.
            """;
    }
}
