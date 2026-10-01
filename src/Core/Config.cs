namespace Aly.Core
{
    /// <summary>
    /// Tunable numbers. Distances are in sprite pixels ("u"), time in seconds.
    /// </summary>
    public sealed class Config
    {
        // body
        public double HalfWidth = 11;
        public double Height = 54;

        // walking: speed must match the walk cycle's stride (see docs/ANIMATION_GUIDE.md):
        // the planted foot moves 2 px per 110 ms frame => 18.2 px/s; running uses 70 ms frames
        public double WalkSpeed = 18.2;
        public double RunSpeed = 28.6;
        public double WalkAccel = 130;
        public double WalkMargin = 24;
        // she keeps to the sides of the screen, out of the way of what you're doing in the middle:
        // strolls about this share of the walkable width on either side (at least SideZoneMin u),
        // and only now and then crosses over to the other side
        public double SideZone = 0.28;
        public double SideZoneMin = 90;
        public double CrossChance = 0.08;
        public double MinStroll = 30;

        // physics
        public double Gravity = 1150;
        public double AirDrag = 0.25;
        public double MaxThrowSpeed = 1400;
        public double WallRestitution = 0.45;
        public double FloorRestitution = 0.28;
        public double BounceMinSpeed = 240;
        public double BounceFriction = 0.7;
        public double GroundFriction = 7;
        public double ThrowWindow = 0.08;
        /// <summary>Landing faster than this leaves her dizzy (and cross).</summary>
        public double DizzySpeed = 620;
        public double DizzySeconds = 2.6;

        // idle behaviour
        public double IdleMin = 2.5;
        public double IdleMax = 7;
        public double SitMin = 8, SitMax = 25;

        // dragging
        public double StruggleHardAfter = 3;
        public double BiteWarnAfter = 8;
        public double BiteAfter = 10;

        // needs (per second)
        public double EnergyDrainPerSecond = 100.0 / (4 * 3600);    // empty after ~4 h awake
        public double EnergyRestorePerSecond = 100.0 / (20 * 60);   // full after ~20 min asleep
        public double MoodBaseline = 65;
        public double MoodDriftPerSecond = 0.02;
        public double TiredEnergy = 30;
        public double ExhaustedEnergy = 15;

        // pokes: annoyance climbs, holds briefly, then decays
        public double PokeAnnoyance = 22;
        public double AnnoyanceHoldSeconds = 1.2;
        public double AnnoyanceDecayPerSecond = 5;
        public double SurprisedAt = 0;
        public double AnnoyedAt = 40;
        public double AngryAt = 66;
        public double ExplodeAt = 100;
        public double SulkSeconds = 30;

        // petting
        public double PetMoodPerSecond = 2;
        public double PetCalmPerSecond = 25;

        // food and the rest of it
        public double FullnessDrainPerSecond = 100.0 / (5 * 3600);  // empty after ~5 h awake
        public double DigestPerSecond = 100.0 / (40 * 60);          // a full stomach digests in ~40 min
        public double DigestToUrge = 1.4;                            // a big meal (50) => toilet (70)
        public double HungryFullness = 30;
        public double StarvingFullness = 12;
        public double FullRefuse = 90;
        public double SelfFeedAfter = 1800;      // starving and unfed this long => gets her own (Character.SelfFood)
        public double ToiletUrge = 70;
        /// <summary>Weight of a fart among idle choices; boosted for a while after a gassy meal (yakiniku…).</summary>
        public double FartWeight = 0.006;
        public double FartGassyBoost = 5;
        public double FartGassyWindow = 3600;

        // the other pet: at most one visit in this many seconds (and only a chance each time she idles)
        public double VisitEvery = 900;

        // 陪看: a laugh or a remark about the video every this many seconds (scaled by 说话频率)
        public double WatchCommentMin = 150, WatchCommentMax = 420;
        // getting out of the way (陪看, 防干扰): further than this (u) from the spot, she trots there
        public double TrotOver = 120;

        // attention
        public double UserIdleSleep = 300;     // seconds without input before she dozes off
        public double StareSeconds = 4;        // cursor resting on her this long => "看什么看"

        // the clipboard sign: a copy made while she was busy waits at most this long
        public double SignPendingSeconds = 60;
        // 常驻显示: she holds the sign this long, then strolls about this long before raising it again
        public double StickyHoldMin = 60, StickyHoldMax = 120;
        public double StickyBreakMin = 20, StickyBreakMax = 45;

        // poke hop (fallback when the atlas has no reaction clips)
        public double HopSpeed = 260;
    }
}
