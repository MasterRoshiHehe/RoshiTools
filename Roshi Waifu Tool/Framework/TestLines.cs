using System;

namespace RoshiWaifuTool.Framework
{
    /// <summary>
    /// Lines for the expression test dialogue. Plain text only: none of the characters the game's dialogue parser
    /// treats as commands (# $ ^ % { } [ ] * and the font's symbol characters).
    /// </summary>
    internal static class TestLines
    {
        private static int Last = -1;

        /// <summary>A random line, never the same one twice in a row.</summary>
        public static string Pick(Random random)
        {
            int index = random.Next(Lines.Length);
            if (index == Last)
                index = (index + 1) % Lines.Length;
            Last = index;
            return Lines[index];
        }

        private static readonly string[] Lines =
        {
            "Is this the face you wanted to see?",
            "Hmm? Do I have something on my face?",
            "Testing, testing... how do I look?",
            "You've been staring for a while now.",
            "One more? Fine, but only because it's you.",
            "This is my good side, right?",
            "Hold on, let me get this one right...",
            "If you say 'just one more' again, I'm counting it.",
            "Did I blink? I feel like I blinked.",
            "Tell me when you've got it.",
            "I can hold this for about three seconds. Hurry.",
            "Do I usually look like this? Be honest.",
            "You're really checking every single one, huh.",
            "My cheeks are starting to hurt.",
            "Is this for science? It feels like science.",
            "I practiced this one in the mirror.",
            "Don't laugh. I mean it.",
            "Okay, okay. Face ready.",
            "How many of these do I even have?",
            "This one's my favourite. Don't tell the others.",
            "I wasn't ready! Do it again.",
            "You could at least say something nice.",
            "Does my hair look alright from there?",
            "I feel like a painting in a gallery.",
            "Somebody drew this face with a lot of care, you know.",
            "Pixel perfect? I hope so.",
            "If something looks off, it's the lighting.",
            "Are you taking notes?",
            "I'm not blushing. It's warm in here.",
            "Next one. Go on, I can take it.",
            "You look so serious when you concentrate.",
            "Is it me, or is it quiet all of a sudden?",
            "I had a dream like this once.",
            "Why do I get the feeling time isn't moving?",
            "The clock has said the same thing for ages.",
            "Did the sun just... stop?",
            "I'd pose better if you gave me a warning.",
            "Is this the part where I say something clever?",
            "Line, please. ...Oh, I'm improvising? Great.",
            "I don't remember what I was going to say.",
            "Words, words, words. There. Happy?",
            "Imagine I said something very charming just now.",
            "This is placeholder talk. Very important placeholder talk.",
            "Blah blah blah. Look at the face, not the text.",
            "Pretend this is a heartfelt confession.",
            "Pretend this is terrible news.",
            "Pretend I just found a coin on the ground.",
            "Pretend you forgot my birthday.",
            "Pretend you remembered my birthday.",
            "Somebody ate the last slice. I have suspicions.",
            "I heard a rumour about you. A good one, mostly.",
            "The weather can't make up its mind today.",
            "Do you ever just stand somewhere and think?",
            "I was on my way somewhere. I think.",
            "My schedule says I should be elsewhere.",
            "I'm fairly sure I was just teleported.",
            "One moment I was at home, and now I'm here.",
            "You didn't see how I got here. Nobody did.",
            "Is it lunchtime yet? It's always almost lunchtime.",
            "I could really go for a snack.",
            "I think I left the stove on.",
            "Don't mind me, I'm only here for the test.",
            "Yes, yes, I'm looking at you.",
            "Eyes up here. Good.",
            "You again! Not that I mind.",
            "Oh! It's you.",
            "What's with that look?",
            "I'm listening. Sort of.",
            "Go on, I'm paying attention.",
            "That's... a lot to take in.",
            "I don't know how to feel about that.",
            "Well, that's something.",
            "You can't be serious.",
            "Really? Really really?",
            "Hah! Good one.",
            "That's not funny. Okay, it's a little funny.",
            "I'll think about it.",
            "Give me a second.",
            "Hm.",
            "Oh no.",
            "Oh yes.",
            "Wait, what?",
            "Say that again, slowly.",
            "I knew it.",
            "I had no idea.",
            "That makes two of us.",
            "You always know what to say.",
            "You never know what to say, do you?",
            "This outfit is new. Do you like it?",
            "I wasn't sure about this outfit, but here we are.",
            "Do these colours suit me?",
            "It took me ages to get ready today.",
            "I just threw this on, honestly.",
            "Someone picked this outfit for me. Guess who.",
            "Careful, I might get used to all this attention.",
            "Are we done? No? Alright.",
            "Last one. I mean it this time.",
            "That was the last one. Probably.",
            "Same time tomorrow?",
            "If this face is wrong, blame the sheet, not me."
        };
    }
}
