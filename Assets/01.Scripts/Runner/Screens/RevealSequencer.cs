using System;
using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Screens
{
    /// <summary>One beat of a reveal. <see cref="Tick"/> returns true once it is over; <see cref="Finish"/> jumps it to its end state (a skip).</summary>
    public interface IRevealStep
    {
        bool Tick(float dt);
        void Finish();
    }

    /// <summary>
    /// A list of <see cref="IRevealStep"/>s played one after the other on
    /// whatever clock the owner ticks it with (the Mission Complete panel
    /// uses unscaled time — it freezes the game under it). Plain C# rather
    /// than coroutines so a skip can resolve in one frame: <see cref="SkipTo"/>
    /// runs every remaining step's <see cref="IRevealStep.Finish"/> up to a
    /// marker, and the rest keep playing. Steps that end at once (an action)
    /// hand the same frame to the next step, so a chain of them never costs
    /// a frame each.
    /// </summary>
    public sealed class RevealSequencer
    {
        readonly List<IRevealStep> steps = new();
        int index;

        public int Count => steps.Count;
        public bool Done => index >= steps.Count;

        public void Add(IRevealStep step) => steps.Add(step);

        public void Tick(float dt)
        {
            while (!Done)
            {
                if (!steps[index].Tick(dt)) return;
                index++;
                dt = 0f; // the next step starts this frame but gets no time of its own
            }
        }

        /// <summary>Finishes every step before <paramref name="count"/> (a value of <see cref="Count"/> taken when the marker step was added).</summary>
        public void SkipTo(int count)
        {
            while (index < count && index < steps.Count)
            {
                steps[index].Finish();
                index++;
            }
        }
    }

    /// <summary>Waits.</summary>
    public sealed class DelayStep : IRevealStep
    {
        readonly float seconds;
        float t;
        public DelayStep(float seconds) => this.seconds = seconds;
        public bool Tick(float dt) { t += dt; return t >= seconds; }
        public void Finish() => t = seconds;
    }

    /// <summary>Runs an action once — on its first tick, or on a skip if it never ticked.</summary>
    public sealed class ActionStep : IRevealStep
    {
        readonly Action action;
        bool ran;
        public ActionStep(Action action) => this.action = action;
        public bool Tick(float dt) { Finish(); return true; }
        public void Finish()
        {
            if (ran) return;
            ran = true;
            action?.Invoke();
        }
    }

    /// <summary>Fades a row in (its <see cref="UI.MenuRow.EntranceAlpha"/>) over a few frames, with an optional kick on arrival.</summary>
    public sealed class RevealRowStep : IRevealStep
    {
        readonly ResultRow row;
        readonly float seconds;
        readonly Action onShown;
        float t;
        bool started;

        public RevealRowStep(ResultRow row, float seconds, Action onShown = null)
        {
            this.row = row;
            this.seconds = Mathf.Max(0.01f, seconds);
            this.onShown = onShown;
        }

        public bool Tick(float dt)
        {
            if (!started) { started = true; onShown?.Invoke(); }
            t += dt;
            row.EntranceAlpha = Mathf.Clamp01(t / seconds);
            return t >= seconds;
        }

        public void Finish()
        {
            if (!started) { started = true; onShown?.Invoke(); }
            t = seconds;
            row.EntranceAlpha = 1f;
        }
    }

    /// <summary>
    /// Types a row's label one character at a time: a block cursor rides the
    /// end of the line, the newest character shows as a random glyph for a
    /// couple of frames before it settles (the decode look), and every
    /// character reports to <see cref="onChar"/> (index, length) so the owner
    /// can punch the row and blip. A skip writes the whole line.
    /// </summary>
    public sealed class TypewriterStep : IRevealStep
    {
        const string Glyphs = "#%&@$<>/\\01";
        const string Cursor = "▌";
        const int ScrambleFrames = 2;

        readonly ResultRow row;
        readonly string text;
        readonly float charsPerSecond;
        readonly Action<int, int> onChar;
        float progress;
        int shown;
        int scramble;
        bool done;

        public TypewriterStep(ResultRow row, string text, float charsPerSecond, Action<int, int> onChar)
        {
            this.row = row;
            this.text = text ?? string.Empty;
            this.charsPerSecond = Mathf.Max(1f, charsPerSecond);
            this.onChar = onChar;
        }

        public bool Tick(float dt)
        {
            if (done) return true;

            progress += charsPerSecond * dt;
            int target = Mathf.Min(text.Length, Mathf.FloorToInt(progress));
            while (shown < target)
            {
                shown++;
                scramble = ScrambleFrames;
                onChar?.Invoke(shown - 1, text.Length);
            }

            if (shown >= text.Length && scramble <= 0)
            {
                Finish();
                return true;
            }

            string visible = text.Substring(0, shown);
            if (scramble > 0 && shown > 0)
            {
                char last = text[shown - 1];
                if (!char.IsWhiteSpace(last))
                    visible = visible.Substring(0, shown - 1) + Glyphs[UnityEngine.Random.Range(0, Glyphs.Length)];
                scramble--;
            }
            row.SetLabelText(shown < text.Length ? visible + Cursor : visible);
            return false;
        }

        public void Finish()
        {
            done = true;
            shown = text.Length;
            scramble = 0;
            row.SetLabelText(text);
        }
    }

    /// <summary>
    /// Counts a row's value from 0 to <paramref name="target"/> with an
    /// ease-out, formatting it every frame and reporting the running value
    /// so the owner can keep a live total. A skip lands on the target.
    /// </summary>
    public sealed class CountUpStep : IRevealStep
    {
        readonly ResultRow row;
        readonly double target;
        readonly float seconds;
        readonly Func<double, string> format;
        readonly Action<double> onValue;
        float t;
        double value;
        bool done;

        public CountUpStep(ResultRow row, double target, float seconds, Func<double, string> format, Action<double> onValue)
        {
            this.row = row;
            this.target = target;
            this.seconds = Mathf.Max(0.05f, seconds);
            this.format = format;
            this.onValue = onValue;
        }

        public bool Tick(float dt)
        {
            if (done) return true;
            t += dt;
            float p = Mathf.Clamp01(t / seconds);
            float e = 1f - (1f - p) * (1f - p) * (1f - p);
            value = target * e;
            row.SetValueText(format(value));
            onValue?.Invoke(value);
            if (p >= 1f) Finish();
            return done;
        }

        public void Finish()
        {
            done = true;
            value = target;
            row.SetValueText(format(value));
            onValue?.Invoke(value);
        }
    }
}
