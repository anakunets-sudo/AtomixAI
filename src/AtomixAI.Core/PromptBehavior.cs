namespace AtomixAI.Core
{
    public enum PromptBehavior
    {
        /// <summary>
        /// Default. The command does not alter the prompt. Standard strict Livvy rules for BIM are applied.
        /// </summary>
        Default,

        /// <summary>
        /// Fill in the remaining context. The instruction's command is carefully attached to the stack.
        /// </summary>
        Append,

        /// <summary>
        /// Complete replacement. Erase all previous instructions and the base prompt. Activate the mode dictated by this command.
        /// </summary>
        Override
    }
}
