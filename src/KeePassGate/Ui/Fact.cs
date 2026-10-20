namespace KeePassGate.Ui
{
    /// <summary>
    /// One row of a table on screen: what it is called, and what it says.
    /// </summary>
    /// <remarks>
    /// A name and a value rather than a sentence, because these are read by scanning rather than by
    /// reading. An operator opening this tab is answering a question they already have, and a
    /// column of names with a column of values answers it by looking, while a paragraph of
    /// sentences that happen to contain the same words has to be parsed first.
    ///
    /// It also removes a decision the wording kept having to make. "Fingerprint x, created d" is
    /// two facts in one line because the line was short, and "0 admission records" is a count with
    /// its noun attached because the noun had nowhere else to go.
    ///
    /// Named for what it is rather than for the first thing that needed it, because more than one
    /// dialog here has a handful of values to put in front of somebody.
    /// </remarks>
    public sealed class Fact
    {
        public Fact(string name, string value)
        {
            Name = name;
            Value = value;
        }

        /// <summary>The left column. No colon: the layout is what separates it from the value.</summary>
        public string Name { get; private set; }

        /// <summary>The right column, which is the part somebody came to read.</summary>
        public string Value { get; private set; }

        /// <summary>
        /// What to call the control showing this fact, so it can be found by what it is rather than
        /// by what it currently says.
        /// </summary>
        /// <remarks>
        /// Built from the fact's own name rather than written down beside it, because a fact added
        /// later would otherwise arrive with no name and nothing would say so. Letters and digits
        /// only, each word starting upper case: what is left is a name rather than a sentence.
        /// </remarks>
        public string ControlName(bool value)
        {
            var built = new System.Text.StringBuilder("KpgFact");
            var starting = true;

            foreach (var letter in Name ?? string.Empty)
            {
                if (!char.IsLetterOrDigit(letter))
                {
                    starting = true;
                    continue;
                }

                built.Append(starting ? char.ToUpperInvariant(letter) : letter);
                starting = false;
            }

            return built.Append(value ? "Value" : "Label").ToString();
        }

        public override string ToString()
        {
            return Name + " " + Value;
        }
    }
}
