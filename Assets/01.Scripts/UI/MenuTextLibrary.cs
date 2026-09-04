using System;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>The languages the menu ships in. Order is the save format — never reorder, only append.</summary>
    public enum MenuLanguage { English = 0, Spanish = 1, Japanese = 2, French = 3 }

    /// <summary>Every string the menu can display. New screens add ids here and entries on the library asset.</summary>
    public enum MenuTextId
    {
        Start, Settings, Cheats, Credits, Exit,
        MasterVolume, MusicVolume, FxVolume, Subtitles, Language,
        On, Off,
        NothingHereYet,
        AreYouSure, Yes, No,
        PressEnter, PressStart,
        HintMove, HintSelect, HintBack, HintChange, HintCancel, HintTitle,
        RoleMaster, RoleFool,
        Paused, Resume, ExitToMenu, QuitGame,
        Debug,
        DebugTabCore, DebugTabMultipliers, DebugTabShipSpeed, DebugTabShipHandling,
        DebugTabShipDash, DebugTabShipHover,
        TrackWidth, Straightness, ReloadScene, ReloadScenePrompt,
        LaunchSpeed, Deceleration, Acceleration, Weight,
        LateralSpeed, SteerResponse, BankAngle, BankResponse,
        DashDistance, DashDuration, DashRecharge, DashGhosts,
        HoverHeight, BobAmplitude, BobFrequency, PitchWobble,
        DebugTabPatrol,
        PatrolBaseSpeed, PatrolRamp, PatrolRubberBand, PatrolCatchUp,
        PatrolStartGap, PatrolCatchDistance, PatrolWarnDistance, PatrolAlertLead, PatrolBoostShare,
        DebugTabCarDrive, DebugTabCarGrip, DebugTabCamera,
        CarMass, CarCenterOfMass, CarDownforce, CarMotorTorque, CarTopSpeed, CarBrakeTorque,
        CarSteerAngle, CarHandbrakeTorque, CarHandbrakeGrip, CarForwardGrip, CarSideGrip,
        CarHillRollback,
        CarPhysicsBackend, CarPhysicsBuiltIn, CarPhysicsEvp,
        CamDistance, CamHeight, CamPitch, CamDamping,
        CamRecenterDelay, CamRecenterSpeed, CamBaseFov, CamSpeedFov,
        CamLookBackAngle, CamLookBackIn, CamLookBackOut, CamLookBackDistance, CamLookBackDamping,
        DebugTabCameraModes, CamModeBlend, CamCloseDistance, CamCloseHeight, CamClosePitch,
        CamFirstPersonForward, CamFirstPersonHeight, CamFirstPersonDamping,
        DebugTabWeather,
        RainIntensity, RainAmount, RainFallSpeed, RainDropSize, RainStreak,
        RainWind, RainWindDirection, RainArea,
        ThunderFrequency, ThunderFlash,
        DebugTabDistanceFog,
        FogIntensity, FogStart, FogEnd, FogDensity, FogSkyAmount, FogHeightFalloff,
        FarGlitchStart, FarGlitchStrength, FarGlitchRate,
        DebugTabSpeedLines,
        SpeedLinesIntensity, SpeedLinesStart, SpeedLinesFull, SpeedLinesDensity, SpeedLinesWidth,
        SpeedLinesInnerMax, SpeedLinesInnerMin, SpeedLinesFlicker, SpeedLinesResponse,
        DebugTabPoliceFleet, DebugTabPoliceChase,
        PolicePatrolCount, PoliceSpawnMin, PoliceSpawnMax, PoliceDespawn,
        PoliceDetection, PoliceLoseSight, PoliceSearchTime,
        PolicePatrolSpeed, PoliceChaseSpeed, PoliceCornerSpeed,
        DebugTabLevel,
        ObjectiveReachSpeed, ObjectiveEscapePolice, ObjectiveGoTo, ObjectiveSurvive,
        CheatEnterCode, CheatUnlocked,
        GameOver, RetryPrompt,
        ObjectiveChaseCar,
        MissionBrief, OptionalChallenges, Reward, Accept,
        HoldToSkip,
        Loading,
        DebugTabAirTime, AirSlowMo, AirSlowMoDelay, AirSlowMoScale, AirSlowMoMinScale, AirSlowMoMaxScale,
        AirSlowMoBlendIn, AirSlowMoBlendOut, AirControlRate, AirControlResponse,
        DebugTabDamage, EvpDamage, EvpDamageWheels, EvpDamageMinSpeed, EvpDamageMultiplier, EvpDamageRadius,
        EvpDamageMaxDisplacement, EvpDamageVertexFracture, EvpDamageWheelBend, EvpDamageRepairRate,
        ObjectiveTimeLimit, ObjectiveHoldFor, ObjectiveDestroy,
        Log, LogSectionGlobal, LogSectionVehicles, LogSectionLastLevel, LogSectionRunner,
        StatPlayTime, StatLevelsCompleted, StatDeaths, StatArrests, StatMaxSpeed, StatMaxJump,
        StatTotaledCars, StatTotaledPolice, StatMoneyEarned, StatBonusObjectives,
        StatLevelName, StatLastObjective, StatOptionalObjectives,
        StatEscapesAttempted, StatEscapesCompleted, StatFastestEscape, StatPowerUps, StatSlowDowns,
        ChallengeBonus, ChallengeDone, ChallengeFailed,
        ObjectiveCollect, LogSectionCollectibles, StatCollectibles,
        ObjectiveJump,
        DebugTabFeatures, FeatureSpacing,
        JumpWidth, JumpLength, JumpAngle, JumpAirDistance, JumpMaxAir, JumpAirControl, JumpSideHitLoss,
        LoopRadius, LoopFallGravity, LoopFallLoss,
        TubeRadius, TubeBand, TubeCurl,
        BarrelRollSeconds,
        MissionComplete, MainObjectives, FiniteRunObjectives, Total, Rank, NextMission, Retry, MissionBonus, StatRank,
        LoseCaught, LoseTimeOut, LoseStalled,
        Store, StoreSectionCar, StoreSectionShip, StoreSectionCharacter, StoreModel, StartMission, Max,
        HintBuy, HintRotate, HintSection,
        UpgradeSpeed, UpgradeAcceleration, UpgradeWeight, UpgradeResistance, UpgradeHandling,
        UpgradeDashPower, UpgradeSpeedMultiplier, UpgradeJumpStrength,
        UpgradeHackingSpeed, UpgradeHackValue, UpgradeStrength, UpgradeRange, UpgradeAccuracy,
        Controls, ControlsSectionShip, ControlsSectionCar, ControlsSectionGeneral,
        PressKey, PressButton, SwappedWith, RestoreDefaults, DefaultsRestored, NoGamepad,
        HintRebind, HintDevice,
        ActionSteerLeft, ActionSteerRight, ActionDashLeft, ActionDashRight,
        ActionAccelerate, ActionBrake, ActionHandbrake, ActionRespawn,
        ActionCityMap, ActionRadioPrevious, ActionRadioNext,
        ActionCameraCycle, ActionLookBack,
        ActionCameraLeft, ActionCameraRight, ActionCameraUp, ActionCameraDown,
        CarBurnoutTorque, CarBurnoutGrip, CarBurnoutMaxSpeed,
        Missions, MissionLabel, MissionNext, StartMissionTarget, RequiresMoney, RequiresUpgrade, ComingSoon, HintPlay
    }

    /// <summary>One menu string in all four languages. Missing translations fall back to English rather than showing blank.</summary>
    [Serializable]
    public struct LocalizedString
    {
        public string english;
        public string spanish;
        public string japanese;
        public string french;

        public LocalizedString(string en, string es, string ja, string fr)
        {
            english = en;
            spanish = es;
            japanese = ja;
            french = fr;
        }

        public string Get(MenuLanguage language)
        {
            string text = language switch
            {
                MenuLanguage.Spanish => spanish,
                MenuLanguage.Japanese => japanese,
                MenuLanguage.French => french,
                _ => english
            };
            return string.IsNullOrEmpty(text) ? english : text;
        }
    }

    /// <summary>
    /// Every text of the main menu in one designer-facing asset, in all four
    /// languages. The menu never hardcodes a display string: widgets fetch by
    /// <see cref="MenuTextId"/> for the current <see cref="UserSettings.Language"/>,
    /// and a <see cref="LocalizedLabel"/> on each Text re-fetches when the
    /// language row changes — so translations are edited here, not in code.
    /// Loaded from Resources like <see cref="MenuTheme"/>; the C# defaults ARE
    /// the shipped translations, so a fresh asset starts fully translated.
    /// </summary>
    [CreateAssetMenu(fileName = "FiniteRunner_MenuTexts", menuName = "FiniteRunner/Menu Text Library")]
    public class MenuTextLibrary : ScriptableObject
    {
        /// <summary>Path inside any Resources folder. Keep in sync with the asset's file name.</summary>
        public const string ResourcePath = "FiniteRunner_MenuTexts";

        [TitleGroup("Main menu rows")]
        [SerializeField] LocalizedString start = new("START", "INICIAR", "スタート", "DÉMARRER");
        [TitleGroup("Main menu rows")]
        [SerializeField] LocalizedString settings = new("SETTINGS", "AJUSTES", "設定", "OPTIONS");
        [TitleGroup("Main menu rows")]
        [SerializeField] LocalizedString cheats = new("CHEATS", "TRUCOS", "チート", "TRICHES");
        [TitleGroup("Main menu rows")]
        [SerializeField] LocalizedString credits = new("CREDITS", "CRÉDITOS", "クレジット", "CRÉDITS");
        [TitleGroup("Main menu rows")]
        [SerializeField] LocalizedString exit = new("EXIT", "SALIR", "終了", "QUITTER");

        [TitleGroup("Settings rows")]
        [SerializeField] LocalizedString masterVolume = new("MASTER VOLUME", "VOLUMEN GENERAL", "マスター音量", "VOLUME GÉNÉRAL");
        [TitleGroup("Settings rows")]
        [SerializeField] LocalizedString musicVolume = new("MUSIC VOLUME", "VOLUMEN DE MÚSICA", "ミュージック音量", "VOLUME MUSIQUE");
        [TitleGroup("Settings rows")]
        [SerializeField] LocalizedString fxVolume = new("FX VOLUME", "VOLUMEN DE EFECTOS", "効果音音量", "VOLUME EFFETS");
        [TitleGroup("Settings rows")]
        [SerializeField] LocalizedString subtitles = new("SUBTITLES", "SUBTÍTULOS", "字幕", "SOUS-TITRES");
        [TitleGroup("Settings rows")]
        [SerializeField] LocalizedString language = new("LANGUAGE", "IDIOMA", "言語", "LANGUE");
        [TitleGroup("Settings rows")]
        [SerializeField] LocalizedString on = new("ON", "SÍ", "オン", "OUI");
        [TitleGroup("Settings rows")]
        [SerializeField] LocalizedString off = new("OFF", "NO", "オフ", "NON");

        [TitleGroup("Screens")]
        [SerializeField] LocalizedString nothingHereYet = new("NOTHING HERE YET", "AQUÍ NO HAY NADA AÚN", "まだ何もない", "RIEN ICI POUR L'INSTANT");
        [TitleGroup("Screens")]
        [SerializeField] LocalizedString areYouSure = new("ARE YOU SURE?", "¿SEGURO QUE QUIERES SALIR?", "本当に終了する？", "VOUS ÊTES SÛR ?");
        [TitleGroup("Screens")]
        [SerializeField] LocalizedString yes = new("YES", "SÍ", "はい", "OUI");
        [TitleGroup("Screens")]
        [SerializeField] LocalizedString no = new("NO", "NO", "いいえ", "NON");

        [TitleGroup("Cheats page")]
        [SerializeField] LocalizedString cheatEnterCode = new("ENTER A CODE", "INTRODUCE UN CÓDIGO", "コードを入力", "ENTREZ UN CODE");
        [TitleGroup("Cheats page")]
        [SerializeField] LocalizedString cheatUnlocked = new("CHEAT UNLOCKED", "TRUCO DESBLOQUEADO", "チート解除", "TRICHE DÉBLOQUÉE");

        [TitleGroup("Attract prompt")]
        [SerializeField] LocalizedString pressEnter = new("PRESS ENTER", "PULSA ENTER", "ENTERキーを押してください", "APPUYEZ SUR ENTRÉE");
        [TitleGroup("Attract prompt")]
        [SerializeField] LocalizedString pressStart = new("PRESS START", "PULSA START", "スタートボタンを押してください", "APPUYEZ SUR START");

        [TitleGroup("Footer hints")]
        [SerializeField] LocalizedString hintMove = new("MOVE", "MOVER", "移動", "DÉPLACER");
        [TitleGroup("Footer hints")]
        [SerializeField] LocalizedString hintSelect = new("SELECT", "ELEGIR", "決定", "VALIDER");
        [TitleGroup("Footer hints")]
        [SerializeField] LocalizedString hintBack = new("BACK", "ATRÁS", "戻る", "RETOUR");
        [TitleGroup("Footer hints")]
        [SerializeField] LocalizedString hintChange = new("CHANGE", "CAMBIAR", "変更", "MODIFIER");
        [TitleGroup("Footer hints")]
        [SerializeField] LocalizedString hintCancel = new("CANCEL", "CANCELAR", "キャンセル", "ANNULER");
        [TitleGroup("Footer hints")]
        [SerializeField] LocalizedString hintTitle = new("TITLE", "TÍTULO", "タイトルへ", "TITRE");
        [TitleGroup("Footer hints")]
        [SerializeField] LocalizedString holdToSkip = new("HOLD TO SKIP", "MANTÉN PARA SALTAR", "長押しでスキップ", "MAINTENIR POUR PASSER");

        [TitleGroup("Credits")]
        [SerializeField] LocalizedString roleMaster = new("MASTER OF DISASTER", "MAESTRO DEL DESASTRE", "ディザスターマスター", "MAÎTRE DU DÉSASTRE");
        [TitleGroup("Credits")]
        [SerializeField] LocalizedString roleFool = new("TOWN FOOL", "EL TONTO DEL PUEBLO", "町の道化師", "L'IDIOT DU VILLAGE");

        [TitleGroup("Pause menu")]
        [SerializeField] LocalizedString paused = new("PAUSED", "PAUSA", "ポーズ", "PAUSE");
        [TitleGroup("Pause menu")]
        [SerializeField] LocalizedString resume = new("RESUME", "CONTINUAR", "再開", "REPRENDRE");
        [TitleGroup("Pause menu")]
        [SerializeField] LocalizedString exitToMenu = new("EXIT TO MAIN MENU", "SALIR AL MENÚ PRINCIPAL", "メインメニューへ", "RETOUR AU MENU");
        [TitleGroup("Pause menu")]
        [SerializeField] LocalizedString quitGame = new("QUIT GAME", "SALIR DEL JUEGO", "ゲームを終了", "QUITTER LE JEU");

        [TitleGroup("Debug menu")]
        [SerializeField] LocalizedString debug = new("DEBUG", "DEPURACIÓN", "デバッグ", "DÉBOGAGE");
        [TitleGroup("Debug menu")]
        [SerializeField] LocalizedString debugTabCore = new("DEBUG — CORE SETTINGS", "DEPURACIÓN — AJUSTES BÁSICOS", "デバッグ — 基本設定", "DÉBOGAGE — RÉGLAGES DE BASE");
        [TitleGroup("Debug menu")]
        [SerializeField] LocalizedString debugTabMultipliers = new("DEBUG — MULTIPLIERS", "DEPURACIÓN — MULTIPLICADORES", "デバッグ — 倍率", "DÉBOGAGE — MULTIPLICATEURS");
        [TitleGroup("Debug menu")]
        [SerializeField] LocalizedString debugTabShipSpeed = new("DEBUG — SHIP SPEED", "DEPURACIÓN — VELOCIDAD DE LA NAVE", "デバッグ — 機体スピード", "DÉBOGAGE — VITESSE DU VAISSEAU");
        [TitleGroup("Debug menu")]
        [SerializeField] LocalizedString debugTabShipHandling = new("DEBUG — SHIP HANDLING", "DEPURACIÓN — MANEJO DE LA NAVE", "デバッグ — 機体ハンドリング", "DÉBOGAGE — MANIABILITÉ DU VAISSEAU");
        [TitleGroup("Debug menu")]
        [SerializeField] LocalizedString debugTabShipDash = new("DEBUG — SHIP DASH", "DEPURACIÓN — DASH DE LA NAVE", "デバッグ — 機体ダッシュ", "DÉBOGAGE — DASH DU VAISSEAU");
        [TitleGroup("Debug menu")]
        [SerializeField] LocalizedString debugTabShipHover = new("DEBUG — SHIP HOVER", "DEPURACIÓN — FLOTACIÓN DE LA NAVE", "デバッグ — 機体ホバー", "DÉBOGAGE — SUSTENTATION DU VAISSEAU");
        [TitleGroup("Debug menu")]
        [SerializeField] LocalizedString trackWidth = new("TRACK WIDTH", "ANCHO DE PISTA", "トラック幅", "LARGEUR DE PISTE");
        [TitleGroup("Debug menu")]
        [SerializeField] LocalizedString straightness = new("STRAIGHTNESS", "RECTITUD", "直線度", "RECTITUDE");
        [TitleGroup("Debug menu")]
        [SerializeField] LocalizedString reloadScene = new("RELOAD SCENE", "RECARGAR ESCENA", "シーンをリロード", "RECHARGER LA SCÈNE");
        [TitleGroup("Debug menu")]
        [SerializeField] LocalizedString reloadScenePrompt = new("DO YOU WANT TO RELOAD THE SCENE?", "¿QUIERES RECARGAR LA ESCENA?", "シーンをリロードしますか？", "VOULEZ-VOUS RECHARGER LA SCÈNE ?");

        [TitleGroup("Ship stats")]
        [SerializeField] LocalizedString launchSpeed = new("LAUNCH SPEED", "VELOCIDAD DE LANZAMIENTO", "発進速度", "VITESSE DE LANCEMENT");
        [TitleGroup("Ship stats")]
        [SerializeField] LocalizedString deceleration = new("DECELERATION", "DESACELERACIÓN", "減速", "DÉCÉLÉRATION");
        [TitleGroup("Ship stats")]
        [SerializeField] LocalizedString acceleration = new("ACCELERATION", "ACELERACIÓN", "加速", "ACCÉLÉRATION");
        [TitleGroup("Ship stats")]
        [SerializeField] LocalizedString weight = new("WEIGHT", "PESO", "重量", "POIDS");
        [TitleGroup("Ship stats")]
        [SerializeField] LocalizedString lateralSpeed = new("LATERAL SPEED", "VELOCIDAD LATERAL", "横移動速度", "VITESSE LATÉRALE");
        [TitleGroup("Ship stats")]
        [SerializeField] LocalizedString steerResponse = new("STEER RESPONSE", "RESPUESTA DE GIRO", "操舵レスポンス", "RÉPONSE DE DIRECTION");
        [TitleGroup("Ship stats")]
        [SerializeField] LocalizedString bankAngle = new("BANK ANGLE", "ÁNGULO DE INCLINACIÓN", "バンク角", "ANGLE D'INCLINAISON");
        [TitleGroup("Ship stats")]
        [SerializeField] LocalizedString bankResponse = new("BANK RESPONSE", "RESPUESTA DE INCLINACIÓN", "バンクレスポンス", "RÉPONSE D'INCLINAISON");
        [TitleGroup("Ship stats")]
        [SerializeField] LocalizedString dashDistance = new("DASH DISTANCE", "DISTANCIA DEL DASH", "ダッシュ距離", "DISTANCE DU DASH");
        [TitleGroup("Ship stats")]
        [SerializeField] LocalizedString dashDuration = new("DASH DURATION", "DURACIÓN DEL DASH", "ダッシュ時間", "DURÉE DU DASH");
        [TitleGroup("Ship stats")]
        [SerializeField] LocalizedString dashRecharge = new("DASH RECHARGE", "RECARGA DEL DASH", "ダッシュ充電", "RECHARGE DU DASH");
        [TitleGroup("Ship stats")]
        [SerializeField] LocalizedString dashGhosts = new("DASH GHOSTS", "ESTELAS DEL DASH", "ダッシュ残像", "FANTÔMES DU DASH");
        [TitleGroup("Ship stats")]
        [SerializeField] LocalizedString barrelRollSeconds = new("BARREL ROLL TIME", "DURACIÓN DEL TONEL", "バレルロール時間", "DURÉE DU TONNEAU");
        [TitleGroup("Ship stats")]
        [SerializeField] LocalizedString hoverHeight = new("HOVER HEIGHT", "ALTURA DE FLOTACIÓN", "ホバー高度", "HAUTEUR DE SUSTENTATION");
        [TitleGroup("Ship stats")]
        [SerializeField] LocalizedString bobAmplitude = new("BOB AMPLITUDE", "AMPLITUD DE BALANCEO", "浮遊の振幅", "AMPLITUDE D'OSCILLATION");
        [TitleGroup("Ship stats")]
        [SerializeField] LocalizedString bobFrequency = new("BOB FREQUENCY", "FRECUENCIA DE BALANCEO", "浮遊の周波数", "FRÉQUENCE D'OSCILLATION");
        [TitleGroup("Ship stats")]
        [SerializeField] LocalizedString pitchWobble = new("PITCH WOBBLE", "CABECEO", "ピッチの揺れ", "TANGAGE");

        [TitleGroup("Patrol stats")]
        [SerializeField] LocalizedString debugTabPatrol = new("DEBUG — PATROL", "DEPURACIÓN — PATRULLA", "デバッグ — パトロール", "DÉBOGAGE — PATROUILLE");
        [TitleGroup("Patrol stats")]
        [SerializeField] LocalizedString patrolBaseSpeed = new("BASE SPEED", "VELOCIDAD BASE", "基本速度", "VITESSE DE BASE");
        [TitleGroup("Patrol stats")]
        [SerializeField] LocalizedString patrolRamp = new("SPEED RAMP", "RAMPA DE VELOCIDAD", "速度上昇率", "MONTÉE EN VITESSE");
        [TitleGroup("Patrol stats")]
        [SerializeField] LocalizedString patrolRubberBand = new("RUBBER BAND", "BANDA ELÁSTICA", "ラバーバンド", "ÉLASTIQUE");
        [TitleGroup("Patrol stats")]
        [SerializeField] LocalizedString patrolCatchUp = new("CATCH-UP ACCEL", "ACELERACIÓN DE ALCANCE", "追従加速", "ACCÉL. DE RATTRAPAGE");
        [TitleGroup("Patrol stats")]
        [SerializeField] LocalizedString patrolStartGap = new("START GAP", "DISTANCIA INICIAL", "開始距離", "ÉCART INITIAL");
        [TitleGroup("Patrol stats")]
        [SerializeField] LocalizedString patrolCatchDistance = new("CATCH DISTANCE", "DISTANCIA DE CAPTURA", "捕獲距離", "DISTANCE DE CAPTURE");
        [TitleGroup("Patrol stats")]
        [SerializeField] LocalizedString patrolWarnDistance = new("WARN DISTANCE", "DISTANCIA DE ALERTA", "警告距離", "DISTANCE D'ALERTE");
        [TitleGroup("Patrol stats")]
        [SerializeField] LocalizedString patrolAlertLead = new("ALERT LEAD", "ADELANTO DE ALERTA", "警告の先行距離", "AVANCE D'ALERTE");
        [TitleGroup("Patrol stats")]
        [SerializeField] LocalizedString patrolBoostShare = new("BOOST SHARE", "PARTE DEL TURBO", "ブースト分配", "PART DU BOOST");

        [TitleGroup("Car stats")]
        [SerializeField] LocalizedString debugTabCarDrive = new("DEBUG — CAR DRIVE", "DEPURACIÓN — MOTOR DEL COCHE", "デバッグ — 車の駆動", "DÉBOGAGE — MOTRICITÉ");
        [TitleGroup("Car stats")]
        [SerializeField] LocalizedString debugTabCarGrip = new("DEBUG — CAR GRIP", "DEPURACIÓN — AGARRE DEL COCHE", "デバッグ — 車のグリップ", "DÉBOGAGE — ADHÉRENCE");
        [TitleGroup("Car stats")]
        [SerializeField] LocalizedString carMass = new("MASS", "MASA", "重量", "MASSE");
        [TitleGroup("Car stats")]
        [SerializeField] LocalizedString carCenterOfMass = new("CENTER OF MASS", "CENTRO DE MASA", "重心の下げ", "CENTRE DE MASSE");
        [TitleGroup("Car stats")]
        [SerializeField] LocalizedString carDownforce = new("DOWNFORCE", "CARGA AERODINÁMICA", "ダウンフォース", "APPUI AÉRO");
        [TitleGroup("Car stats")]
        [SerializeField] LocalizedString carMotorTorque = new("MOTOR TORQUE", "PAR DEL MOTOR", "エンジントルク", "COUPLE MOTEUR");
        [TitleGroup("Car stats")]
        [SerializeField] LocalizedString carTopSpeed = new("TOP SPEED", "VELOCIDAD MÁXIMA", "最高速度", "VITESSE MAX");
        [TitleGroup("Car stats")]
        [SerializeField] LocalizedString carBrakeTorque = new("BRAKE TORQUE", "PAR DE FRENADO", "ブレーキトルク", "COUPLE DE FREIN");
        [TitleGroup("Car stats")]
        [SerializeField] LocalizedString carHillRollback = new("HILL ASSIST", "AYUDA EN PENDIENTE", "ヒルアシスト", "ASSIST. EN CÔTE");
        [TitleGroup("Car stats")]
        [SerializeField] LocalizedString carSteerAngle = new("STEER ANGLE", "ÁNGULO DE GIRO", "操舵角", "ANGLE DE BRAQUAGE");
        [TitleGroup("Car stats")]
        [SerializeField] LocalizedString carHandbrakeTorque = new("HANDBRAKE TORQUE", "PAR DEL FRENO DE MANO", "ハンドブレーキトルク", "COUPLE DE FREIN À MAIN");
        [TitleGroup("Car stats")]
        [SerializeField] LocalizedString carHandbrakeGrip = new("HANDBRAKE GRIP", "AGARRE CON FRENO DE MANO", "ハンドブレーキ時グリップ", "ADHÉRENCE FREIN À MAIN");
        [TitleGroup("Car stats")]
        [SerializeField] LocalizedString carForwardGrip = new("FORWARD GRIP", "AGARRE LONGITUDINAL", "前後グリップ", "ADHÉRENCE LONGITUDINALE");
        [TitleGroup("Car stats")]
        [SerializeField] LocalizedString carSideGrip = new("SIDE GRIP", "AGARRE LATERAL", "横方向グリップ", "ADHÉRENCE LATÉRALE");
        [TitleGroup("Car stats")]
        [SerializeField] LocalizedString carBurnoutTorque = new("BURNOUT TORQUE", "PAR DE BURNOUT", "バーンアウトトルク", "COUPLE DE BURNOUT");
        [TitleGroup("Car stats")]
        [SerializeField] LocalizedString carBurnoutGrip = new("BURNOUT GRIP", "AGARRE EN BURNOUT", "バーンアウト時グリップ", "ADHÉRENCE BURNOUT");
        [TitleGroup("Car stats")]
        [SerializeField] LocalizedString carBurnoutMaxSpeed = new("BURNOUT MAX SPEED", "VEL. MÁX. DE BURNOUT", "バーンアウト上限速度", "VITESSE MAX BURNOUT");
        [TitleGroup("Car stats")]
        [SerializeField] LocalizedString carPhysicsBackend = new("VEHICLE PHYSICS", "FÍSICA DE VEHÍCULOS", "車両物理エンジン", "PHYSIQUE DES VÉHICULES");
        [TitleGroup("Car stats")]
        [SerializeField] LocalizedString carPhysicsBuiltIn = new("BUILT-IN", "INTEGRADA", "標準", "INTÉGRÉE");
        [TitleGroup("Car stats")]
        [SerializeField] LocalizedString carPhysicsEvp = new("EVP", "EVP", "EVP", "EVP");
        [TitleGroup("Air time")]
        [SerializeField] LocalizedString debugTabAirTime = new("AIR TIME", "TIEMPO EN EL AIRE", "エアタイム", "TEMPS EN L'AIR");
        [TitleGroup("Air time")]
        [SerializeField] LocalizedString airSlowMo = new("AIR SLOW-MO", "CÁMARA LENTA EN EL AIRE", "空中スローモーション", "RALENTI EN L'AIR");
        [TitleGroup("Air time")]
        [SerializeField] LocalizedString airSlowMoDelay = new("AIR DELAY", "RETARDO EN EL AIRE", "発動までの滞空時間", "DÉLAI EN L'AIR");
        [TitleGroup("Air time")]
        [SerializeField] LocalizedString airSlowMoScale = new("SLOW-MO SPEED", "VELOCIDAD CÁMARA LENTA", "スロー速度", "VITESSE DU RALENTI");
        [TitleGroup("Air time")]
        [SerializeField] LocalizedString airSlowMoMinScale = new("SLOWEST SPEED", "VELOCIDAD MÍNIMA", "最遅速度", "VITESSE MINIMALE");
        [TitleGroup("Air time")]
        [SerializeField] LocalizedString airSlowMoMaxScale = new("FASTEST SPEED", "VELOCIDAD MÁXIMA", "最速速度", "VITESSE MAXIMALE");
        [TitleGroup("Air time")]
        [SerializeField] LocalizedString airSlowMoBlendIn = new("SLOW-MO FADE IN", "ENTRADA CÁMARA LENTA", "スロー開始フェード", "FONDU D'ENTRÉE");
        [TitleGroup("Air time")]
        [SerializeField] LocalizedString airSlowMoBlendOut = new("SLOW-MO FADE OUT", "SALIDA CÁMARA LENTA", "スロー終了フェード", "FONDU DE SORTIE");
        [TitleGroup("Air time")]
        [SerializeField] LocalizedString airControlRate = new("AIR CONTROL RATE", "GIRO EN EL AIRE", "空中操作速度", "ROTATION EN L'AIR");
        [TitleGroup("Air time")]
        [SerializeField] LocalizedString airControlResponse = new("AIR CONTROL RESPONSE", "RESPUESTA EN EL AIRE", "空中操作の反応", "RÉPONSE EN L'AIR");

        [TitleGroup("Body damage")]
        [SerializeField] LocalizedString debugTabDamage = new("BODY DAMAGE", "DAÑOS EN LA CARROCERÍA", "車体ダメージ", "DÉGÂTS CARROSSERIE");
        [TitleGroup("Body damage")]
        [SerializeField] LocalizedString evpDamage = new("BODY DAMAGE", "DAÑOS EN LA CARROCERÍA", "車体ダメージ", "DÉGÂTS CARROSSERIE");
        [TitleGroup("Body damage")]
        [SerializeField] LocalizedString evpDamageWheels = new("BENT WHEELS", "RUEDAS TORCIDAS", "ホイールの歪み", "ROUES VOILÉES");
        [TitleGroup("Body damage")]
        [SerializeField] LocalizedString evpDamageMinSpeed = new("DENT MIN SPEED", "VELOCIDAD MÍN. DE ABOLLADO", "凹み最低速度", "VITESSE MIN. DE BOSSE");
        [TitleGroup("Body damage")]
        [SerializeField] LocalizedString evpDamageMultiplier = new("DENT STRENGTH", "FUERZA DE ABOLLADO", "凹みの強さ", "FORCE DES BOSSES");
        [TitleGroup("Body damage")]
        [SerializeField] LocalizedString evpDamageRadius = new("DENT RADIUS", "RADIO DE ABOLLADO", "凹みの半径", "RAYON DES BOSSES");
        [TitleGroup("Body damage")]
        [SerializeField] LocalizedString evpDamageMaxDisplacement = new("MAX DENT DEPTH", "PROFUNDIDAD MÁXIMA", "凹みの最大深さ", "PROFONDEUR MAX");
        [TitleGroup("Body damage")]
        [SerializeField] LocalizedString evpDamageVertexFracture = new("PANEL TEARING", "DESGARRO DE CHAPA", "パネルの裂け", "DÉCHIRURE DES TÔLES");
        [TitleGroup("Body damage")]
        [SerializeField] LocalizedString evpDamageWheelBend = new("MAX WHEEL BEND", "TORSIÓN MÁX. DE RUEDA", "ホイール最大傾き", "VOILAGE MAX DES ROUES");
        [TitleGroup("Body damage")]
        [SerializeField] LocalizedString evpDamageRepairRate = new("REPAIR SPEED", "VELOCIDAD DE REPARACIÓN", "修理速度", "VITESSE DE RÉPARATION");

        [TitleGroup("Chase camera stats")]
        [SerializeField] LocalizedString debugTabCamera = new("DEBUG — CHASE CAMERA", "DEPURACIÓN — CÁMARA", "デバッグ — カメラ", "DÉBOGAGE — CAMÉRA");
        [TitleGroup("Chase camera stats")]
        [SerializeField] LocalizedString camDistance = new("CAMERA DISTANCE", "DISTANCIA DE CÁMARA", "カメラ距離", "DISTANCE CAMÉRA");
        [TitleGroup("Chase camera stats")]
        [SerializeField] LocalizedString camHeight = new("LOOK HEIGHT", "ALTURA DE MIRA", "注視点の高さ", "HAUTEUR DE VISÉE");
        [TitleGroup("Chase camera stats")]
        [SerializeField] LocalizedString camPitch = new("DEFAULT PITCH", "INCLINACIÓN POR DEFECTO", "既定の俯角", "TANGAGE PAR DÉFAUT");
        [TitleGroup("Chase camera stats")]
        [SerializeField] LocalizedString camDamping = new("POSITION DAMPING", "AMORTIGUACIÓN", "位置ダンピング", "AMORTISSEMENT");
        [TitleGroup("Chase camera stats")]
        [SerializeField] LocalizedString camRecenterDelay = new("RECENTER DELAY", "RETARDO DE RECENTRADO", "再センタリング遅延", "DÉLAI DE RECENTRAGE");
        [TitleGroup("Chase camera stats")]
        [SerializeField] LocalizedString camRecenterSpeed = new("RECENTER SPEED", "VELOCIDAD DE RECENTRADO", "再センタリング速度", "VITESSE DE RECENTRAGE");
        [TitleGroup("Chase camera stats")]
        [SerializeField] LocalizedString camBaseFov = new("BASE FOV", "FOV BASE", "基本FOV", "FOV DE BASE");
        [TitleGroup("Chase camera stats")]
        [SerializeField] LocalizedString camSpeedFov = new("SPEED FOV", "FOV POR VELOCIDAD", "速度FOV", "FOV VITESSE");
        [TitleGroup("Chase camera stats")]
        [SerializeField] LocalizedString camLookBackAngle = new("LOOK BACK ANGLE", "ÁNGULO DE MIRAR ATRÁS", "後方視の角度", "ANGLE REGARD ARRIÈRE");
        [TitleGroup("Chase camera stats")]
        [SerializeField] LocalizedString camLookBackIn = new("LOOK BACK TIME", "TIEMPO DE GIRO ATRÁS", "後方視の時間", "TEMPS REGARD ARRIÈRE");
        [TitleGroup("Chase camera stats")]
        [SerializeField] LocalizedString camLookBackOut = new("LOOK BACK RETURN", "REGRESO DE MIRAR ATRÁS", "後方視の復帰", "RETOUR REGARD ARRIÈRE");
        [TitleGroup("Chase camera stats")]
        [SerializeField] LocalizedString camLookBackDistance = new("LOOK BACK DISTANCE", "DISTANCIA DE MIRAR ATRÁS", "後方視の距離", "DISTANCE REGARD ARRIÈRE");
        [TitleGroup("Chase camera stats")]
        [SerializeField] LocalizedString camLookBackDamping = new("LOOK BACK DAMPING", "AMORTIGUACIÓN DE MIRAR ATRÁS", "後方視のダンピング", "AMORTISSEMENT REGARD ARRIÈRE");
        [SerializeField] LocalizedString debugTabCameraModes = new("DEBUG — CAMERA MODES", "DEPURACIÓN — MODOS DE CÁMARA", "デバッグ — カメラモード", "DÉBOGAGE — MODES CAMÉRA");
        [SerializeField] LocalizedString camModeBlend = new("MODE BLEND", "TRANSICIÓN DE MODO", "モード切替時間", "FONDU DE MODE");
        [SerializeField] LocalizedString camCloseDistance = new("CLOSE DISTANCE", "DISTANCIA CERCANA", "近距離", "DISTANCE PROCHE");
        [SerializeField] LocalizedString camCloseHeight = new("CLOSE HEIGHT", "ALTURA CERCANA", "近距離の高さ", "HAUTEUR PROCHE");
        [SerializeField] LocalizedString camClosePitch = new("CLOSE PITCH", "INCLINACIÓN CERCANA", "近距離の角度", "INCLINAISON PROCHE");
        [SerializeField] LocalizedString camFirstPersonForward = new("FIRST PERSON FORWARD", "AVANCE EN PRIMERA PERSONA", "一人称の前方位置", "AVANCE PREMIÈRE PERSONNE");
        [SerializeField] LocalizedString camFirstPersonHeight = new("FIRST PERSON HEIGHT", "ALTURA EN PRIMERA PERSONA", "一人称の高さ", "HAUTEUR PREMIÈRE PERSONNE");
        [SerializeField] LocalizedString camFirstPersonDamping = new("FIRST PERSON DAMPING", "AMORTIGUACIÓN EN PRIMERA PERSONA", "一人称の減衰", "AMORTISSEMENT PREMIÈRE PERSONNE");

        [TitleGroup("Weather stats")]
        [SerializeField] LocalizedString debugTabWeather = new("DEBUG — WEATHER", "DEPURACIÓN — CLIMA", "デバッグ — 天候", "DÉBOGAGE — MÉTÉO");
        [TitleGroup("Weather stats")]
        [SerializeField] LocalizedString rainIntensity = new("RAIN INTENSITY", "INTENSIDAD DE LLUVIA", "雨の強さ", "INTENSITÉ DE PLUIE");
        [TitleGroup("Weather stats")]
        [SerializeField] LocalizedString rainAmount = new("DROPS PER SECOND", "GOTAS POR SEGUNDO", "毎秒の雨粒", "GOUTTES PAR SECONDE");
        [TitleGroup("Weather stats")]
        [SerializeField] LocalizedString rainFallSpeed = new("FALL SPEED", "VELOCIDAD DE CAÍDA", "落下速度", "VITESSE DE CHUTE");
        [TitleGroup("Weather stats")]
        [SerializeField] LocalizedString rainDropSize = new("DROP SIZE", "TAMAÑO DE GOTA", "雨粒の大きさ", "TAILLE DES GOUTTES");
        [TitleGroup("Weather stats")]
        [SerializeField] LocalizedString rainStreak = new("STREAK LENGTH", "LARGO DEL TRAZO", "雨脚の長さ", "LONGUEUR DES TRAÎNÉES");
        [TitleGroup("Weather stats")]
        [SerializeField] LocalizedString rainWind = new("WIND SPEED", "VELOCIDAD DEL VIENTO", "風速", "VITESSE DU VENT");
        [TitleGroup("Weather stats")]
        [SerializeField] LocalizedString rainWindDirection = new("WIND DIRECTION", "DIRECCIÓN DEL VIENTO", "風向き", "DIRECTION DU VENT");
        [TitleGroup("Weather stats")]
        [SerializeField] LocalizedString rainArea = new("RAIN RADIUS", "RADIO DE LLUVIA", "降雨半径", "RAYON DE PLUIE");
        [TitleGroup("Weather stats")]
        [SerializeField] LocalizedString thunderFrequency = new("THUNDER RATE", "FRECUENCIA DE TRUENOS", "雷の頻度", "FRÉQUENCE DU TONNERRE");
        [TitleGroup("Weather stats")]
        [SerializeField] LocalizedString thunderFlash = new("FLASH STRENGTH", "FUERZA DEL DESTELLO", "閃光の強さ", "FORCE DE L'ÉCLAIR");

        [TitleGroup("Fog stats")]
        [SerializeField] LocalizedString debugTabDistanceFog = new("DEBUG — FOG", "DEPURACIÓN — NIEBLA", "デバッグ — 霧", "DÉBOGAGE — BROUILLARD");
        [TitleGroup("Fog stats")]
        [SerializeField] LocalizedString fogIntensity = new("FOG INTENSITY", "INTENSIDAD DE NIEBLA", "霧の強さ", "INTENSITÉ DU BROUILLARD");
        [TitleGroup("Fog stats")]
        [SerializeField] LocalizedString fogStart = new("FOG START", "INICIO DE NIEBLA", "霧の開始距離", "DÉBUT DU BROUILLARD");
        [TitleGroup("Fog stats")]
        [SerializeField] LocalizedString fogEnd = new("FOG END", "FIN DE NIEBLA", "霧の終了距離", "FIN DU BROUILLARD");
        [TitleGroup("Fog stats")]
        [SerializeField] LocalizedString fogDensity = new("FOG THICKNESS", "ESPESOR DE NIEBLA", "霧の濃さ", "ÉPAISSEUR DU BROUILLARD");
        [TitleGroup("Fog stats")]
        [SerializeField] LocalizedString fogSkyAmount = new("SKY FOG", "NIEBLA EN EL CIELO", "空の霧", "BROUILLARD DU CIEL");
        [TitleGroup("Fog stats")]
        [SerializeField] LocalizedString fogHeightFalloff = new("HEIGHT FALLOFF", "ATENUACIÓN POR ALTURA", "高さによる減衰", "ATTÉNUATION EN HAUTEUR");
        [TitleGroup("Fog stats")]
        [SerializeField] LocalizedString farGlitchStart = new("GLITCH START", "INICIO DEL GLITCH", "グリッチの開始距離", "DÉBUT DU GLITCH");
        [TitleGroup("Fog stats")]
        [SerializeField] LocalizedString farGlitchStrength = new("GLITCH STRENGTH", "FUERZA DEL GLITCH", "グリッチの強さ", "FORCE DU GLITCH");
        [TitleGroup("Fog stats")]
        [SerializeField] LocalizedString farGlitchRate = new("GLITCH RATE", "FRECUENCIA DEL GLITCH", "グリッチの頻度", "FRÉQUENCE DU GLITCH");

        [TitleGroup("Speed lines stats")]
        [SerializeField] LocalizedString debugTabSpeedLines = new("DEBUG — SPEED LINES", "DEPURACIÓN — LÍNEAS DE VELOCIDAD", "デバッグ — 集中線", "DÉBOGAGE — LIGNES DE VITESSE");
        [TitleGroup("Speed lines stats")]
        [SerializeField] LocalizedString speedLinesIntensity = new("LINES INTENSITY", "INTENSIDAD DE LÍNEAS", "集中線の強さ", "INTENSITÉ DES LIGNES");
        [TitleGroup("Speed lines stats")]
        [SerializeField] LocalizedString speedLinesStart = new("START SPEED", "VELOCIDAD DE INICIO", "開始速度", "VITESSE DE DÉBUT");
        [TitleGroup("Speed lines stats")]
        [SerializeField] LocalizedString speedLinesFull = new("FULL SPEED", "VELOCIDAD PLENA", "最大速度", "VITESSE PLEINE");
        [TitleGroup("Speed lines stats")]
        [SerializeField] LocalizedString speedLinesDensity = new("LINE DENSITY", "DENSIDAD DE LÍNEAS", "線の密度", "DENSITÉ DES LIGNES");
        [TitleGroup("Speed lines stats")]
        [SerializeField] LocalizedString speedLinesWidth = new("LINE WIDTH", "GROSOR DE LÍNEA", "線の太さ", "LARGEUR DES LIGNES");
        [TitleGroup("Speed lines stats")]
        [SerializeField] LocalizedString speedLinesInnerMax = new("CLEAR RADIUS (SLOW)", "RADIO LIBRE (LENTO)", "中央の空き（低速）", "RAYON LIBRE (LENT)");
        [TitleGroup("Speed lines stats")]
        [SerializeField] LocalizedString speedLinesInnerMin = new("CLEAR RADIUS (FAST)", "RADIO LIBRE (RÁPIDO)", "中央の空き（高速）", "RAYON LIBRE (RAPIDE)");
        [TitleGroup("Speed lines stats")]
        [SerializeField] LocalizedString speedLinesFlicker = new("FLICKER RATE", "FRECUENCIA DE PARPADEO", "ちらつきの頻度", "FRÉQUENCE DE SCINTILLEMENT");
        [TitleGroup("Speed lines stats")]
        [SerializeField] LocalizedString speedLinesResponse = new("RESPONSE", "RESPUESTA", "反応速度", "RÉACTIVITÉ");

        [TitleGroup("City police stats")]
        [SerializeField] LocalizedString debugTabPoliceFleet = new("DEBUG — POLICE FLEET", "DEPURACIÓN — FLOTA POLICIAL", "デバッグ — 警察の台数", "DÉBOGAGE — FLOTTE DE POLICE");
        [TitleGroup("City police stats")]
        [SerializeField] LocalizedString debugTabPoliceChase = new("DEBUG — POLICE CHASE", "DEPURACIÓN — PERSECUCIÓN", "デバッグ — 追跡", "DÉBOGAGE — POURSUITE");
        [TitleGroup("City police stats")]
        [SerializeField] LocalizedString policePatrolCount = new("PATROL COUNT", "NÚMERO DE PATRULLAS", "パトカーの数", "NOMBRE DE PATROUILLES");
        [TitleGroup("City police stats")]
        [SerializeField] LocalizedString policeSpawnMin = new("SPAWN MIN", "APARICIÓN MÍN", "出現距離 最小", "APPARITION MIN");
        [TitleGroup("City police stats")]
        [SerializeField] LocalizedString policeSpawnMax = new("SPAWN MAX", "APARICIÓN MÁX", "出現距離 最大", "APPARITION MAX");
        [TitleGroup("City police stats")]
        [SerializeField] LocalizedString policeDespawn = new("DESPAWN DISTANCE", "DISTANCIA DE RETIRADA", "消滅距離", "DISTANCE DE RETRAIT");
        [TitleGroup("City police stats")]
        [SerializeField] LocalizedString policeDetection = new("DETECTION RANGE", "ALCANCE DE DETECCIÓN", "発見距離", "PORTÉE DE DÉTECTION");
        [TitleGroup("City police stats")]
        [SerializeField] LocalizedString policeLoseSight = new("LOSE SIGHT", "PERDER DE VISTA", "見失う時間", "PERTE DE VUE");
        [TitleGroup("City police stats")]
        [SerializeField] LocalizedString policeSearchTime = new("SEARCH TIME", "TIEMPO DE BÚSQUEDA", "捜索時間", "TEMPS DE RECHERCHE");
        [TitleGroup("City police stats")]
        [SerializeField] LocalizedString policePatrolSpeed = new("PATROL SPEED", "VELOCIDAD DE PATRULLA", "巡回速度", "VITESSE DE PATROUILLE");
        [TitleGroup("City police stats")]
        [SerializeField] LocalizedString policeChaseSpeed = new("CHASE SPEED", "VELOCIDAD DE PERSECUCIÓN", "追跡速度", "VITESSE DE POURSUITE");
        [TitleGroup("City police stats")]
        [SerializeField] LocalizedString policeCornerSpeed = new("CORNER SPEED", "VELOCIDAD EN CURVA", "コーナー速度", "VITESSE EN VIRAGE");

        [TitleGroup("City level objectives")]
        [SerializeField] LocalizedString debugTabLevel = new("DEBUG — LEVEL", "DEPURACIÓN — NIVEL", "デバッグ — レベル", "DÉBOGAGE — NIVEAU");
        [TitleGroup("City level objectives")]
        [SerializeField] LocalizedString objectiveReachSpeed = new("REACH SPEED", "ALCANZAR VELOCIDAD", "速度到達", "ATTEINDRE LA VITESSE");
        [TitleGroup("City level objectives")]
        [SerializeField] LocalizedString objectiveEscapePolice = new("ESCAPE POLICE", "ESCAPAR DE LA POLICÍA", "警察から逃げる", "SEMER LA POLICE");
        [TitleGroup("City level objectives")]
        [SerializeField] LocalizedString objectiveGoTo = new("GO TO", "IR A", "目的地へ", "ALLER À");
        [TitleGroup("City level objectives")]
        [SerializeField] LocalizedString objectiveSurvive = new("SURVIVE", "SOBREVIVIR", "生き延びる", "SURVIVRE");
        [TitleGroup("City level objectives")]
        [SerializeField] LocalizedString objectiveChaseCar = new("CHASE", "PERSEGUIR", "追跡", "POURSUIVRE");
        [TitleGroup("City level objectives")]
        [SerializeField] LocalizedString objectiveTimeLimit = new("TIME LIMIT", "TIEMPO LÍMITE", "制限時間", "TEMPS LIMITE");
        [TitleGroup("City level objectives")]
        [SerializeField] LocalizedString objectiveHoldFor = new("HOLD FOR", "MANTENER", "維持", "MAINTENIR");
        [TitleGroup("City level objectives")]
        [SerializeField] LocalizedString objectiveDestroy = new("DESTROY", "DESTRUIR", "破壊", "DÉTRUIRE");
        [TitleGroup("City level objectives")]
        [SerializeField] LocalizedString objectiveCollect = new("COLLECT", "RECOGER", "回収", "RAMASSER");
        [TitleGroup("City level objectives")]
        [SerializeField] LocalizedString objectiveJump = new("JUMP", "SALTO", "ジャンプ", "SAUT");

        // Debug menu — FEATURES tab (runner track features: jump ramps)
        [SerializeField] LocalizedString debugTabFeatures = new("FEATURES", "ELEMENTOS", "ギミック", "ÉLÉMENTS");
        [SerializeField] LocalizedString featureSpacing = new("FEATURE SPACING", "SEPARACIÓN", "ギミック間隔", "ESPACEMENT");
        [SerializeField] LocalizedString jumpWidth = new("RAMP WIDTH", "ANCHO DE RAMPA", "ランプ幅", "LARGEUR DE RAMPE");
        [SerializeField] LocalizedString jumpLength = new("RAMP LENGTH", "LARGO DE RAMPA", "ランプ長", "LONGUEUR DE RAMPE");
        [SerializeField] LocalizedString jumpAngle = new("RAMP ANGLE", "ÁNGULO DE RAMPA", "ランプ角度", "ANGLE DE RAMPE");
        [SerializeField] LocalizedString jumpAirDistance = new("AIR PER SPEED", "VUELO POR VELOCIDAD", "速度あたり飛距離", "VOL PAR VITESSE");
        [SerializeField] LocalizedString jumpMaxAir = new("MAX AIR DISTANCE", "VUELO MÁXIMO", "最大飛距離", "VOL MAXIMUM");
        [SerializeField] LocalizedString jumpAirControl = new("AIR CONTROL", "CONTROL AÉREO", "空中操作", "CONTRÔLE AÉRIEN");
        [SerializeField] LocalizedString jumpSideHitLoss = new("SIDE HIT LOSS", "PÉRDIDA LATERAL", "側面衝突ロス", "PERTE LATÉRALE");
        [SerializeField] LocalizedString loopRadius = new("LOOP RADIUS", "RADIO DEL LOOP", "ループ半径", "RAYON DU LOOPING");
        [SerializeField] LocalizedString loopFallGravity = new("FALL GRAVITY", "GRAVEDAD DE CAÍDA", "落下重力", "GRAVITÉ DE CHUTE");
        [SerializeField] LocalizedString loopFallLoss = new("FALL SPEED LOSS", "PÉRDIDA POR CAÍDA", "落下速度ロス", "PERTE DE CHUTE");
        [SerializeField] LocalizedString tubeRadius = new("TUBE RADIUS", "RADIO DEL TUBO", "チューブ半径", "RAYON DU TUBE");
        [SerializeField] LocalizedString tubeBand = new("TUBE BAND", "BANDA DEL TUBO", "チューブ可動域", "BANDE DU TUBE");
        [SerializeField] LocalizedString tubeCurl = new("TUBE CURL", "CURVATURA DEL TUBO", "チューブ巻き込み", "COURBURE DU TUBE");

        [TitleGroup("Game over")]
        [SerializeField] LocalizedString gameOver = new("GAME OVER", "FIN DE LA PARTIDA", "ゲームオーバー", "PARTIE TERMINÉE");
        [TitleGroup("Game over")]
        [SerializeField] LocalizedString retryPrompt = new("RETRY?", "¿REINTENTAR?", "リトライしますか？", "RÉESSAYER ?");
        [TitleGroup("Game over")]
        [SerializeField] LocalizedString loseCaught = new("CAUGHT BY THE PATROL", "ATRAPADO POR LA PATRULLA", "パトロールに捕まった", "RATTRAPÉ PAR LA PATROUILLE");
        [TitleGroup("Game over")]
        [SerializeField] LocalizedString loseTimeOut = new("TIME RAN OUT", "SE ACABÓ EL TIEMPO", "時間切れ", "TEMPS ÉCOULÉ");
        [TitleGroup("Game over")]
        [SerializeField] LocalizedString loseStalled = new("OUT OF SPEED", "SIN VELOCIDAD", "速度切れ", "PLUS DE VITESSE");

        // The Store: section titles, the model row, the purchase rows and the
        // upgrade categories. Category labels are kept short on purpose — the
        // purchase row reserves a wide zone for its ten pips and the price, so
        // a long translation would push the plate off the left edge.
        [TitleGroup("Store")]
        [SerializeField] LocalizedString store = new("STORE", "TIENDA", "ストア", "BOUTIQUE");
        [TitleGroup("Store")]
        [SerializeField] LocalizedString storeSectionCar = new("STORE — CAR", "TIENDA — COCHE", "ストア — 車", "BOUTIQUE — VOITURE");
        [TitleGroup("Store")]
        [SerializeField] LocalizedString storeSectionShip = new("STORE — SHIP", "TIENDA — NAVE", "ストア — 機体", "BOUTIQUE — VAISSEAU");
        [TitleGroup("Store")]
        [SerializeField] LocalizedString storeSectionCharacter = new("STORE — CHARACTER", "TIENDA — PERSONAJE", "ストア — キャラクター", "BOUTIQUE — PERSONNAGE");
        [TitleGroup("Store")]
        [SerializeField] LocalizedString storeModel = new("MODEL", "MODELO", "モデル", "MODÈLE");
        [TitleGroup("Store")]
        [SerializeField] LocalizedString startMission = new("START MISSION", "INICIAR MISIÓN", "ミッション開始", "LANCER LA MISSION");
        [TitleGroup("Store")]
        [SerializeField] LocalizedString max = new("MAX", "MÁX", "最大", "MAX");
        [TitleGroup("Store")]
        [SerializeField] LocalizedString hintBuy = new("BUY", "COMPRAR", "購入", "ACHETER");
        [TitleGroup("Store")]
        [SerializeField] LocalizedString hintRotate = new("ROTATE", "GIRAR", "回転", "PIVOTER");
        [TitleGroup("Store")]
        [SerializeField] LocalizedString hintSection = new("SECTION", "SECCIÓN", "セクション", "SECTION");
        [TitleGroup("Store")]
        [SerializeField] LocalizedString upgradeSpeed = new("SPEED", "VELOCIDAD", "スピード", "VITESSE");
        [TitleGroup("Store")]
        [SerializeField] LocalizedString upgradeAcceleration = new("ACCELERATION", "ACELERACIÓN", "加速", "ACCÉLÉRATION");
        [TitleGroup("Store")]
        [SerializeField] LocalizedString upgradeWeight = new("WEIGHT", "PESO", "重量", "POIDS");
        [TitleGroup("Store")]
        [SerializeField] LocalizedString upgradeResistance = new("RESISTANCE", "RESISTENCIA", "耐久", "RÉSISTANCE");
        [TitleGroup("Store")]
        [SerializeField] LocalizedString upgradeHandling = new("HANDLING", "MANEJO", "ハンドリング", "MANIABILITÉ");
        [TitleGroup("Store")]
        [SerializeField] LocalizedString upgradeDashPower = new("DASH POWER", "POTENCIA DASH", "ダッシュ力", "PUISSANCE DASH");
        [TitleGroup("Store")]
        [SerializeField] LocalizedString upgradeSpeedMultiplier = new("SPEED MULTIPLIER", "MULT. VELOCIDAD", "スピード倍率", "MULT. VITESSE");
        [TitleGroup("Store")]
        [SerializeField] LocalizedString upgradeJumpStrength = new("JUMP STRENGTH", "FUERZA DE SALTO", "ジャンプ力", "FORCE DE SAUT");
        [TitleGroup("Store")]
        [SerializeField] LocalizedString upgradeHackingSpeed = new("HACKING SPEED", "VEL. DE HACKEO", "ハッキング速度", "VITESSE PIRATAGE");
        [TitleGroup("Store")]
        [SerializeField] LocalizedString upgradeHackValue = new("HACK VALUE", "VALOR DE HACKEO", "ハッキング価値", "VALEUR PIRATAGE");
        [TitleGroup("Store")]
        [SerializeField] LocalizedString upgradeStrength = new("STRENGTH", "FUERZA", "筋力", "FORCE");
        [TitleGroup("Store")]
        [SerializeField] LocalizedString upgradeRange = new("RANGE", "ALCANCE", "射程", "PORTÉE");
        [TitleGroup("Store")]
        [SerializeField] LocalizedString upgradeAccuracy = new("ACCURACY", "PRECISIÓN", "命中率", "PRÉCISION");

        // The campaign: the main menu's MISSIONS map, the Store's START MISSION
        // target, the requirement a locked mission prints, and the Coming Soon
        // scene. The {n} placeholders are string.Format slots — positional, so
        // a translation may reorder them but never drop one.
        [TitleGroup("Campaign")]
        [SerializeField] LocalizedString missions = new("MISSIONS", "MISIONES", "ミッション", "MISSIONS");
        [TitleGroup("Campaign")]
        [SerializeField] LocalizedString missionLabel = new("MISSION {0} — {1}", "MISIÓN {0} — {1}", "ミッション{0} — {1}", "MISSION {0} — {1}");
        [TitleGroup("Campaign")]
        [SerializeField] LocalizedString missionNext = new("NEXT", "SIGUIENTE", "次", "SUIVANTE");
        [TitleGroup("Campaign")]
        [SerializeField] LocalizedString startMissionTarget = new("START MISSION — {0}: {1}", "INICIAR MISIÓN — {0}: {1}", "ミッション開始 — {0}: {1}", "LANCER LA MISSION — {0} : {1}");
        [TitleGroup("Campaign")]
        [SerializeField] LocalizedString requiresMoney = new("REQUIRES: {0}", "REQUIERE: {0}", "必要: {0}", "REQUIS : {0}");
        [TitleGroup("Campaign")]
        [SerializeField] LocalizedString requiresUpgrade = new("REQUIRES: {0} LV {1}", "REQUIERE: {0} NV {1}", "必要: {0} LV{1}", "REQUIS : {0} NIV {1}");
        [TitleGroup("Campaign")]
        [SerializeField] LocalizedString comingSoon = new("COMING SOON", "PRÓXIMAMENTE", "近日公開", "BIENTÔT DISPONIBLE");
        [TitleGroup("Campaign")]
        [SerializeField] LocalizedString hintPlay = new("PLAY", "JUGAR", "プレイ", "JOUER");

        [TitleGroup("Controls")]
        [SerializeField] LocalizedString controls = new("CONTROLS", "CONTROLES", "操作設定", "COMMANDES");
        [TitleGroup("Controls")]
        [SerializeField] LocalizedString controlsSectionShip = new("SHIP", "NAVE", "シップ", "VAISSEAU");
        [TitleGroup("Controls")]
        [SerializeField] LocalizedString controlsSectionCar = new("CAR", "COCHE", "クルマ", "VOITURE");
        [TitleGroup("Controls")]
        [SerializeField] LocalizedString controlsSectionGeneral = new("GENERAL", "GENERAL", "全般", "GÉNÉRAL");
        [TitleGroup("Controls")]
        [SerializeField] LocalizedString pressKey = new("PRESS A KEY…", "PULSA UNA TECLA…", "キーを押してください…", "APPUYEZ SUR UNE TOUCHE…");
        [TitleGroup("Controls")]
        [SerializeField] LocalizedString pressButton = new("PRESS A BUTTON…", "PULSA UN BOTÓN…", "ボタンを押してください…", "APPUYEZ SUR UN BOUTON…");
        [TitleGroup("Controls")]
        [SerializeField] LocalizedString swappedWith = new("SWAPPED WITH {0}", "INTERCAMBIADO CON {0}", "{0} と入れ替えました", "ÉCHANGÉ AVEC {0}");
        [TitleGroup("Controls")]
        [SerializeField] LocalizedString restoreDefaults = new("RESTORE DEFAULTS", "RESTAURAR VALORES", "初期設定に戻す", "RÉTABLIR PAR DÉFAUT");
        [TitleGroup("Controls")]
        [SerializeField] LocalizedString defaultsRestored = new("DEFAULTS RESTORED", "VALORES RESTAURADOS", "初期設定に戻しました", "VALEURS PAR DÉFAUT RÉTABLIES");
        [TitleGroup("Controls")]
        [SerializeField] LocalizedString noGamepad = new("NO GAMEPAD CONNECTED", "NO HAY MANDO CONECTADO", "ゲームパッドが接続されていません", "AUCUNE MANETTE CONNECTÉE");
        [TitleGroup("Controls")]
        [SerializeField] LocalizedString hintRebind = new("REBIND", "REASIGNAR", "再割り当て", "RÉASSIGNER");
        [TitleGroup("Controls")]
        [SerializeField] LocalizedString hintDevice = new("DEVICE", "DISPOSITIVO", "デバイス", "APPAREIL");
        [TitleGroup("Controls")]
        [SerializeField] LocalizedString actionSteerLeft = new("STEER LEFT", "GIRAR IZQUIERDA", "左に操舵", "BRAQUER À GAUCHE");
        [TitleGroup("Controls")]
        [SerializeField] LocalizedString actionSteerRight = new("STEER RIGHT", "GIRAR DERECHA", "右に操舵", "BRAQUER À DROITE");
        [TitleGroup("Controls")]
        [SerializeField] LocalizedString actionDashLeft = new("DASH LEFT", "IMPULSO IZQUIERDA", "左ダッシュ", "DASH À GAUCHE");
        [TitleGroup("Controls")]
        [SerializeField] LocalizedString actionDashRight = new("DASH RIGHT", "IMPULSO DERECHA", "右ダッシュ", "DASH À DROITE");
        [TitleGroup("Controls")]
        [SerializeField] LocalizedString actionAccelerate = new("ACCELERATE", "ACELERAR", "アクセル", "ACCÉLÉRER");
        [TitleGroup("Controls")]
        [SerializeField] LocalizedString actionBrake = new("BRAKE / REVERSE", "FRENO / MARCHA ATRÁS", "ブレーキ / バック", "FREIN / MARCHE ARRIÈRE");
        [TitleGroup("Controls")]
        [SerializeField] LocalizedString actionHandbrake = new("HANDBRAKE", "FRENO DE MANO", "ハンドブレーキ", "FREIN À MAIN");
        [TitleGroup("Controls")]
        [SerializeField] LocalizedString actionRespawn = new("RESPAWN", "REAPARECER", "リスポーン", "RÉAPPARAÎTRE");
        [TitleGroup("Controls")]
        [SerializeField] LocalizedString actionCityMap = new("MAP", "MAPA", "マップ", "CARTE");
        [TitleGroup("Controls")]
        [SerializeField] LocalizedString actionRadioPrevious = new("RADIO PREVIOUS", "RADIO ANTERIOR", "ラジオ 前へ", "RADIO PRÉCÉDENT");
        [TitleGroup("Controls")]
        [SerializeField] LocalizedString actionRadioNext = new("RADIO NEXT", "RADIO SIGUIENTE", "ラジオ 次へ", "RADIO SUIVANT");
        [TitleGroup("Controls")]
        [SerializeField] LocalizedString actionCameraCycle = new("CAMERA VIEW", "VISTA DE CÁMARA", "カメラ視点", "VUE CAMÉRA");
        [TitleGroup("Controls")]
        [SerializeField] LocalizedString actionLookBack = new("LOOK BACK", "MIRAR ATRÁS", "後方を見る", "REGARDER DERRIÈRE");
        [TitleGroup("Controls")]
        [SerializeField] LocalizedString actionCameraLeft = new("CAMERA LEFT", "CÁMARA IZQUIERDA", "カメラ 左", "CAMÉRA GAUCHE");
        [TitleGroup("Controls")]
        [SerializeField] LocalizedString actionCameraRight = new("CAMERA RIGHT", "CÁMARA DERECHA", "カメラ 右", "CAMÉRA DROITE");
        [TitleGroup("Controls")]
        [SerializeField] LocalizedString actionCameraUp = new("CAMERA UP", "CÁMARA ARRIBA", "カメラ 上", "CAMÉRA HAUT");
        [TitleGroup("Controls")]
        [SerializeField] LocalizedString actionCameraDown = new("CAMERA DOWN", "CÁMARA ABAJO", "カメラ 下", "CAMÉRA BAS");

        [TitleGroup("Loading screen")]
        [SerializeField] LocalizedString loading = new("LOADING...", "CARGANDO...", "ロード中...", "CHARGEMENT...");

        [TitleGroup("Mission brief")]
        [SerializeField] LocalizedString missionBrief = new("MISSION BRIEF", "INFORME DE MISIÓN", "ミッションブリーフ", "BRIEFING DE MISSION");
        [TitleGroup("Mission brief")]
        [SerializeField] LocalizedString optionalChallenges = new("OPTIONAL CHALLENGES", "DESAFÍOS OPCIONALES", "追加チャレンジ", "DÉFIS OPTIONNELS");
        [TitleGroup("Mission brief")]
        [SerializeField] LocalizedString reward = new("REWARD", "RECOMPENSA", "報酬", "RÉCOMPENSE");
        [TitleGroup("Mission brief")]
        [SerializeField] LocalizedString accept = new("ACCEPT", "ACEPTAR", "承諾", "ACCEPTER");
        [TitleGroup("Mission brief")]
        [SerializeField] LocalizedString challengeBonus = new("BONUS", "EXTRA", "ボーナス", "BONUS");
        [TitleGroup("Mission brief")]
        [SerializeField] LocalizedString challengeDone = new("DONE", "HECHO", "完了", "FAIT");
        [TitleGroup("Mission brief")]
        [SerializeField] LocalizedString challengeFailed = new("FAILED", "FALLIDO", "失敗", "ÉCHOUÉ");

        [TitleGroup("Mission complete")]
        [SerializeField] LocalizedString missionComplete = new("MISSION COMPLETE", "MISIÓN COMPLETADA", "ミッション完了", "MISSION ACCOMPLIE");
        [TitleGroup("Mission complete")]
        [SerializeField] LocalizedString mainObjectives = new("MAIN OBJECTIVES", "OBJETIVOS PRINCIPALES", "メイン目標", "OBJECTIFS PRINCIPAUX");
        [TitleGroup("Mission complete")]
        [SerializeField] LocalizedString finiteRunObjectives = new("ESCAPE RUN", "CARRERA DE HUIDA", "エスケープラン", "COURSE D'ÉVASION");
        [TitleGroup("Mission complete")]
        [SerializeField] LocalizedString total = new("TOTAL", "TOTAL", "合計", "TOTAL");
        [TitleGroup("Mission complete")]
        [SerializeField] LocalizedString rank = new("RANK", "RANGO", "ランク", "RANG");
        [TitleGroup("Mission complete")]
        [SerializeField] LocalizedString nextMission = new("NEXT MISSION", "SIGUIENTE MISIÓN", "次のミッション", "MISSION SUIVANTE");
        [TitleGroup("Mission complete")]
        [SerializeField] LocalizedString retry = new("RETRY", "REINTENTAR", "リトライ", "RÉESSAYER");
        [TitleGroup("Mission complete")]
        [SerializeField] LocalizedString missionBonus = new("MISSION BONUS", "BONO DE MISIÓN", "ミッションボーナス", "PRIME DE MISSION");
        [TitleGroup("Mission complete")]
        [SerializeField] LocalizedString statRank = new("RANK", "RANGO", "ランク", "RANG");

        [TitleGroup("Log")]
        [SerializeField] LocalizedString log = new("LOG", "REGISTRO", "ログ", "JOURNAL");
        [TitleGroup("Log")]
        [SerializeField] LocalizedString logSectionGlobal = new("GLOBAL", "GLOBAL", "全体", "GLOBAL");
        [TitleGroup("Log")]
        [SerializeField] LocalizedString logSectionVehicles = new("TOTALED VEHICLES", "VEHÍCULOS DESTROZADOS", "破壊した車両", "VÉHICULES DÉTRUITS");
        [TitleGroup("Log")]
        [SerializeField] LocalizedString logSectionCollectibles = new("COLLECTIBLES", "COLECCIONABLES", "収集アイテム", "OBJETS À COLLECTIONNER");
        [TitleGroup("Log")]
        [SerializeField] LocalizedString statCollectibles = new("TOTAL COLLECTED", "TOTAL RECOGIDO", "総収集数", "TOTAL RAMASSÉ");
        [TitleGroup("Log")]
        [SerializeField] LocalizedString logSectionLastLevel = new("LAST LEVEL", "ÚLTIMO NIVEL", "前回のレベル", "DERNIER NIVEAU");
        [TitleGroup("Log")]
        [SerializeField] LocalizedString logSectionRunner = new("ESCAPE RUNS", "HUIDAS", "逃走ラン", "ÉVASIONS");
        [TitleGroup("Log")]
        [SerializeField] LocalizedString statPlayTime = new("TOTAL PLAY TIME", "TIEMPO TOTAL DE JUEGO", "総プレイ時間", "TEMPS DE JEU TOTAL");
        [TitleGroup("Log")]
        [SerializeField] LocalizedString statLevelsCompleted = new("LEVELS COMPLETED", "NIVELES COMPLETADOS", "クリアしたレベル", "NIVEAUX TERMINÉS");
        [TitleGroup("Log")]
        [SerializeField] LocalizedString statDeaths = new("TOTAL DEATHS", "MUERTES TOTALES", "総死亡回数", "MORTS TOTALES");
        [TitleGroup("Log")]
        [SerializeField] LocalizedString statArrests = new("TIMES ARRESTED", "VECES ARRESTADO", "逮捕回数", "ARRESTATIONS");
        [TitleGroup("Log")]
        [SerializeField] LocalizedString statMaxSpeed = new("MAX SPEED", "VELOCIDAD MÁXIMA", "最高速度", "VITESSE MAX");
        [TitleGroup("Log")]
        [SerializeField] LocalizedString statMaxJump = new("LONGEST JUMP", "SALTO MÁS LARGO", "最長ジャンプ", "PLUS LONG SAUT");
        [TitleGroup("Log")]
        [SerializeField] LocalizedString statTotaledCars = new("TOTALED CARS", "COCHES DESTROZADOS", "破壊した車", "VOITURES DÉTRUITES");
        [TitleGroup("Log")]
        [SerializeField] LocalizedString statTotaledPolice = new("TOTALED POLICE CARS", "PATRULLAS DESTROZADAS", "破壊したパトカー", "VOITURES DE POLICE DÉTRUITES");
        [TitleGroup("Log")]
        [SerializeField] LocalizedString statMoneyEarned = new("MONEY EARNED", "DINERO GANADO", "獲得金額", "ARGENT GAGNÉ");
        [TitleGroup("Log")]
        [SerializeField] LocalizedString statBonusObjectives = new("BONUS OBJECTIVES", "OBJETIVOS EXTRA", "ボーナス目標", "OBJECTIFS BONUS");
        [TitleGroup("Log")]
        [SerializeField] LocalizedString statLevelName = new("LEVEL", "NIVEL", "レベル", "NIVEAU");
        [TitleGroup("Log")]
        [SerializeField] LocalizedString statLastObjective = new("LAST OBJECTIVE", "ÚLTIMO OBJETIVO", "最後の目標", "DERNIER OBJECTIF");
        [TitleGroup("Log")]
        [SerializeField] LocalizedString statOptionalObjectives = new("OPTIONAL OBJECTIVES", "OBJETIVOS OPCIONALES", "任意目標", "OBJECTIFS OPTIONNELS");
        [TitleGroup("Log")]
        [SerializeField] LocalizedString statEscapesAttempted = new("ESCAPES ATTEMPTED", "HUIDAS INTENTADAS", "逃走試行", "ÉVASIONS TENTÉES");
        [TitleGroup("Log")]
        [SerializeField] LocalizedString statEscapesCompleted = new("ESCAPES COMPLETED", "HUIDAS LOGRADAS", "逃走成功", "ÉVASIONS RÉUSSIES");
        [TitleGroup("Log")]
        [SerializeField] LocalizedString statFastestEscape = new("FASTEST ESCAPE", "HUIDA MÁS RÁPIDA", "最速逃走", "ÉVASION LA PLUS RAPIDE");
        [TitleGroup("Log")]
        [SerializeField] LocalizedString statPowerUps = new("POWER-UPS COLLECTED", "POTENCIADORES RECOGIDOS", "取得したパワーアップ", "BONUS RAMASSÉS");
        [TitleGroup("Log")]
        [SerializeField] LocalizedString statSlowDowns = new("SLOW-DOWNS COLLECTED", "RALENTIZADORES RECOGIDOS", "取得したスローダウン", "MALUS RAMASSÉS");

        static MenuTextLibrary cached;

        /// <summary>The library asset, or a throwaway on the C# defaults if none is in a Resources folder.</summary>
        public static MenuTextLibrary Load()
        {
            if (cached != null) return cached;

            cached = Resources.Load<MenuTextLibrary>(ResourcePath);
            if (cached == null)
            {
                Debug.LogWarning($"No {nameof(MenuTextLibrary)} at Resources/{ResourcePath} — using the built-in defaults.");
                cached = CreateInstance<MenuTextLibrary>();
            }
            return cached;
        }

        /// <summary>The string for an id in the given language.</summary>
        public string Get(MenuTextId id, MenuLanguage lang) => Entry(id).Get(lang);

        /// <summary>The string for an id in the player's current language.</summary>
        public string Get(MenuTextId id) => Get(id, UserSettings.Language);

        // Shared scratch generator for width measurements — never renders.
        static readonly TextGenerator measurer = new();

        /// <summary>
        /// Widest rendering of an id across every shipped language, in UI
        /// units. Menu boxes are sized off this instead of the current
        /// language, so plates keep one uniform width and no translation
        /// ever clips — switching language never resizes the menu.
        /// </summary>
        public float MaxWidth(MenuTextId id, Font font, int fontSize)
        {
            float widest = 0f;
            foreach (MenuLanguage lang in System.Enum.GetValues(typeof(MenuLanguage)))
                widest = Mathf.Max(widest, MeasureWidth(Get(id, lang), font, fontSize));
            return widest;
        }

        /// <summary>Rendered width of one string, for texts that never localize (debug labels).</summary>
        public static float MeasureWidth(string text, Font font, int fontSize)
        {
            if (string.IsNullOrEmpty(text) || font == null) return 0f;

            var settings = new TextGenerationSettings
            {
                font = font,
                fontSize = fontSize,
                fontStyle = FontStyle.Normal,
                richText = false,
                scaleFactor = 1f,
                color = Color.white,
                lineSpacing = 1f,
                textAnchor = TextAnchor.MiddleCenter,
                horizontalOverflow = HorizontalWrapMode.Overflow,
                verticalOverflow = VerticalWrapMode.Overflow,
                generationExtents = Vector2.zero,
                pivot = new Vector2(0.5f, 0.5f)
            };
            return measurer.GetPreferredWidth(text, settings);
        }

        /// <summary>
        /// How a language names itself on the selector row — always in its own
        /// language, so a player lost in the wrong one can still find home.
        /// </summary>
        public static string LanguageDisplayName(MenuLanguage lang) => lang switch
        {
            MenuLanguage.Spanish => "ESPAÑOL",
            MenuLanguage.Japanese => "日本語",
            MenuLanguage.French => "FRANÇAIS",
            _ => "ENGLISH"
        };

        LocalizedString Entry(MenuTextId id) => id switch
        {
            MenuTextId.Start => start,
            MenuTextId.Settings => settings,
            MenuTextId.Cheats => cheats,
            MenuTextId.Credits => credits,
            MenuTextId.Exit => exit,
            MenuTextId.MasterVolume => masterVolume,
            MenuTextId.MusicVolume => musicVolume,
            MenuTextId.FxVolume => fxVolume,
            MenuTextId.Subtitles => subtitles,
            MenuTextId.Language => language,
            MenuTextId.On => on,
            MenuTextId.Off => off,
            MenuTextId.NothingHereYet => nothingHereYet,
            MenuTextId.AreYouSure => areYouSure,
            MenuTextId.Yes => yes,
            MenuTextId.No => no,
            MenuTextId.PressEnter => pressEnter,
            MenuTextId.PressStart => pressStart,
            MenuTextId.HintMove => hintMove,
            MenuTextId.HintSelect => hintSelect,
            MenuTextId.HintBack => hintBack,
            MenuTextId.HintChange => hintChange,
            MenuTextId.HintCancel => hintCancel,
            MenuTextId.HintTitle => hintTitle,
            MenuTextId.RoleMaster => roleMaster,
            MenuTextId.RoleFool => roleFool,
            MenuTextId.Paused => paused,
            MenuTextId.Resume => resume,
            MenuTextId.ExitToMenu => exitToMenu,
            MenuTextId.QuitGame => quitGame,
            MenuTextId.Debug => debug,
            MenuTextId.DebugTabCore => debugTabCore,
            MenuTextId.DebugTabMultipliers => debugTabMultipliers,
            MenuTextId.DebugTabShipSpeed => debugTabShipSpeed,
            MenuTextId.DebugTabShipHandling => debugTabShipHandling,
            MenuTextId.DebugTabShipDash => debugTabShipDash,
            MenuTextId.DebugTabShipHover => debugTabShipHover,
            MenuTextId.TrackWidth => trackWidth,
            MenuTextId.Straightness => straightness,
            MenuTextId.ReloadScene => reloadScene,
            MenuTextId.ReloadScenePrompt => reloadScenePrompt,
            MenuTextId.LaunchSpeed => launchSpeed,
            MenuTextId.Deceleration => deceleration,
            MenuTextId.Acceleration => acceleration,
            MenuTextId.Weight => weight,
            MenuTextId.LateralSpeed => lateralSpeed,
            MenuTextId.SteerResponse => steerResponse,
            MenuTextId.BankAngle => bankAngle,
            MenuTextId.BankResponse => bankResponse,
            MenuTextId.DashDistance => dashDistance,
            MenuTextId.DashDuration => dashDuration,
            MenuTextId.DashRecharge => dashRecharge,
            MenuTextId.DashGhosts => dashGhosts,
            MenuTextId.BarrelRollSeconds => barrelRollSeconds,
            MenuTextId.HoverHeight => hoverHeight,
            MenuTextId.BobAmplitude => bobAmplitude,
            MenuTextId.BobFrequency => bobFrequency,
            MenuTextId.PitchWobble => pitchWobble,
            MenuTextId.DebugTabPatrol => debugTabPatrol,
            MenuTextId.PatrolBaseSpeed => patrolBaseSpeed,
            MenuTextId.PatrolRamp => patrolRamp,
            MenuTextId.PatrolRubberBand => patrolRubberBand,
            MenuTextId.PatrolCatchUp => patrolCatchUp,
            MenuTextId.PatrolStartGap => patrolStartGap,
            MenuTextId.PatrolCatchDistance => patrolCatchDistance,
            MenuTextId.PatrolWarnDistance => patrolWarnDistance,
            MenuTextId.PatrolAlertLead => patrolAlertLead,
            MenuTextId.PatrolBoostShare => patrolBoostShare,
            MenuTextId.DebugTabCarDrive => debugTabCarDrive,
            MenuTextId.DebugTabCarGrip => debugTabCarGrip,
            MenuTextId.CarMass => carMass,
            MenuTextId.CarCenterOfMass => carCenterOfMass,
            MenuTextId.CarDownforce => carDownforce,
            MenuTextId.CarMotorTorque => carMotorTorque,
            MenuTextId.CarTopSpeed => carTopSpeed,
            MenuTextId.CarBrakeTorque => carBrakeTorque,
            MenuTextId.CarHillRollback => carHillRollback,
            MenuTextId.CarPhysicsBackend => carPhysicsBackend,
            MenuTextId.CarPhysicsBuiltIn => carPhysicsBuiltIn,
            MenuTextId.CarPhysicsEvp => carPhysicsEvp,
            MenuTextId.CarSteerAngle => carSteerAngle,
            MenuTextId.CarHandbrakeTorque => carHandbrakeTorque,
            MenuTextId.CarHandbrakeGrip => carHandbrakeGrip,
            MenuTextId.CarForwardGrip => carForwardGrip,
            MenuTextId.CarSideGrip => carSideGrip,
            MenuTextId.CarBurnoutTorque => carBurnoutTorque,
            MenuTextId.CarBurnoutGrip => carBurnoutGrip,
            MenuTextId.CarBurnoutMaxSpeed => carBurnoutMaxSpeed,
            MenuTextId.DebugTabAirTime => debugTabAirTime,
            MenuTextId.AirSlowMo => airSlowMo,
            MenuTextId.AirSlowMoDelay => airSlowMoDelay,
            MenuTextId.AirSlowMoScale => airSlowMoScale,
            MenuTextId.AirSlowMoMinScale => airSlowMoMinScale,
            MenuTextId.AirSlowMoMaxScale => airSlowMoMaxScale,
            MenuTextId.AirSlowMoBlendIn => airSlowMoBlendIn,
            MenuTextId.AirSlowMoBlendOut => airSlowMoBlendOut,
            MenuTextId.AirControlRate => airControlRate,
            MenuTextId.AirControlResponse => airControlResponse,
            MenuTextId.DebugTabDamage => debugTabDamage,
            MenuTextId.EvpDamage => evpDamage,
            MenuTextId.EvpDamageWheels => evpDamageWheels,
            MenuTextId.EvpDamageMinSpeed => evpDamageMinSpeed,
            MenuTextId.EvpDamageMultiplier => evpDamageMultiplier,
            MenuTextId.EvpDamageRadius => evpDamageRadius,
            MenuTextId.EvpDamageMaxDisplacement => evpDamageMaxDisplacement,
            MenuTextId.EvpDamageVertexFracture => evpDamageVertexFracture,
            MenuTextId.EvpDamageWheelBend => evpDamageWheelBend,
            MenuTextId.EvpDamageRepairRate => evpDamageRepairRate,
            MenuTextId.DebugTabCamera => debugTabCamera,
            MenuTextId.CamDistance => camDistance,
            MenuTextId.CamHeight => camHeight,
            MenuTextId.CamPitch => camPitch,
            MenuTextId.CamDamping => camDamping,
            MenuTextId.CamRecenterDelay => camRecenterDelay,
            MenuTextId.CamRecenterSpeed => camRecenterSpeed,
            MenuTextId.CamBaseFov => camBaseFov,
            MenuTextId.CamSpeedFov => camSpeedFov,
            MenuTextId.CamLookBackAngle => camLookBackAngle,
            MenuTextId.CamLookBackIn => camLookBackIn,
            MenuTextId.CamLookBackOut => camLookBackOut,
            MenuTextId.CamLookBackDistance => camLookBackDistance,
            MenuTextId.CamLookBackDamping => camLookBackDamping,
            MenuTextId.DebugTabCameraModes => debugTabCameraModes,
            MenuTextId.CamModeBlend => camModeBlend,
            MenuTextId.CamCloseDistance => camCloseDistance,
            MenuTextId.CamCloseHeight => camCloseHeight,
            MenuTextId.CamClosePitch => camClosePitch,
            MenuTextId.CamFirstPersonForward => camFirstPersonForward,
            MenuTextId.CamFirstPersonHeight => camFirstPersonHeight,
            MenuTextId.CamFirstPersonDamping => camFirstPersonDamping,
            MenuTextId.DebugTabWeather => debugTabWeather,
            MenuTextId.RainIntensity => rainIntensity,
            MenuTextId.RainAmount => rainAmount,
            MenuTextId.RainFallSpeed => rainFallSpeed,
            MenuTextId.RainDropSize => rainDropSize,
            MenuTextId.RainStreak => rainStreak,
            MenuTextId.RainWind => rainWind,
            MenuTextId.RainWindDirection => rainWindDirection,
            MenuTextId.RainArea => rainArea,
            MenuTextId.ThunderFrequency => thunderFrequency,
            MenuTextId.ThunderFlash => thunderFlash,
            MenuTextId.DebugTabDistanceFog => debugTabDistanceFog,
            MenuTextId.FogIntensity => fogIntensity,
            MenuTextId.FogStart => fogStart,
            MenuTextId.FogEnd => fogEnd,
            MenuTextId.FogDensity => fogDensity,
            MenuTextId.FogSkyAmount => fogSkyAmount,
            MenuTextId.FogHeightFalloff => fogHeightFalloff,
            MenuTextId.FarGlitchStart => farGlitchStart,
            MenuTextId.FarGlitchStrength => farGlitchStrength,
            MenuTextId.FarGlitchRate => farGlitchRate,
            MenuTextId.DebugTabSpeedLines => debugTabSpeedLines,
            MenuTextId.SpeedLinesIntensity => speedLinesIntensity,
            MenuTextId.SpeedLinesStart => speedLinesStart,
            MenuTextId.SpeedLinesFull => speedLinesFull,
            MenuTextId.SpeedLinesDensity => speedLinesDensity,
            MenuTextId.SpeedLinesWidth => speedLinesWidth,
            MenuTextId.SpeedLinesInnerMax => speedLinesInnerMax,
            MenuTextId.SpeedLinesInnerMin => speedLinesInnerMin,
            MenuTextId.SpeedLinesFlicker => speedLinesFlicker,
            MenuTextId.SpeedLinesResponse => speedLinesResponse,
            MenuTextId.DebugTabPoliceFleet => debugTabPoliceFleet,
            MenuTextId.DebugTabPoliceChase => debugTabPoliceChase,
            MenuTextId.PolicePatrolCount => policePatrolCount,
            MenuTextId.PoliceSpawnMin => policeSpawnMin,
            MenuTextId.PoliceSpawnMax => policeSpawnMax,
            MenuTextId.PoliceDespawn => policeDespawn,
            MenuTextId.PoliceDetection => policeDetection,
            MenuTextId.PoliceLoseSight => policeLoseSight,
            MenuTextId.PoliceSearchTime => policeSearchTime,
            MenuTextId.PolicePatrolSpeed => policePatrolSpeed,
            MenuTextId.PoliceChaseSpeed => policeChaseSpeed,
            MenuTextId.PoliceCornerSpeed => policeCornerSpeed,
            MenuTextId.DebugTabLevel => debugTabLevel,
            MenuTextId.ObjectiveReachSpeed => objectiveReachSpeed,
            MenuTextId.ObjectiveEscapePolice => objectiveEscapePolice,
            MenuTextId.ObjectiveGoTo => objectiveGoTo,
            MenuTextId.ObjectiveSurvive => objectiveSurvive,
            MenuTextId.ObjectiveChaseCar => objectiveChaseCar,
            MenuTextId.ObjectiveTimeLimit => objectiveTimeLimit,
            MenuTextId.ObjectiveHoldFor => objectiveHoldFor,
            MenuTextId.ObjectiveDestroy => objectiveDestroy,
            MenuTextId.ObjectiveCollect => objectiveCollect,
            MenuTextId.ObjectiveJump => objectiveJump,
            MenuTextId.DebugTabFeatures => debugTabFeatures,
            MenuTextId.FeatureSpacing => featureSpacing,
            MenuTextId.JumpWidth => jumpWidth,
            MenuTextId.JumpLength => jumpLength,
            MenuTextId.JumpAngle => jumpAngle,
            MenuTextId.JumpAirDistance => jumpAirDistance,
            MenuTextId.JumpMaxAir => jumpMaxAir,
            MenuTextId.JumpAirControl => jumpAirControl,
            MenuTextId.JumpSideHitLoss => jumpSideHitLoss,
            MenuTextId.LoopRadius => loopRadius,
            MenuTextId.LoopFallGravity => loopFallGravity,
            MenuTextId.LoopFallLoss => loopFallLoss,
            MenuTextId.TubeRadius => tubeRadius,
            MenuTextId.TubeBand => tubeBand,
            MenuTextId.TubeCurl => tubeCurl,
            MenuTextId.LogSectionCollectibles => logSectionCollectibles,
            MenuTextId.StatCollectibles => statCollectibles,
            MenuTextId.CheatEnterCode => cheatEnterCode,
            MenuTextId.CheatUnlocked => cheatUnlocked,
            MenuTextId.GameOver => gameOver,
            MenuTextId.RetryPrompt => retryPrompt,
            MenuTextId.LoseCaught => loseCaught,
            MenuTextId.LoseTimeOut => loseTimeOut,
            MenuTextId.LoseStalled => loseStalled,
            MenuTextId.Store => store,
            MenuTextId.StoreSectionCar => storeSectionCar,
            MenuTextId.StoreSectionShip => storeSectionShip,
            MenuTextId.StoreSectionCharacter => storeSectionCharacter,
            MenuTextId.StoreModel => storeModel,
            MenuTextId.StartMission => startMission,
            MenuTextId.Max => max,
            MenuTextId.HintBuy => hintBuy,
            MenuTextId.HintRotate => hintRotate,
            MenuTextId.HintSection => hintSection,
            MenuTextId.UpgradeSpeed => upgradeSpeed,
            MenuTextId.UpgradeAcceleration => upgradeAcceleration,
            MenuTextId.UpgradeWeight => upgradeWeight,
            MenuTextId.UpgradeResistance => upgradeResistance,
            MenuTextId.UpgradeHandling => upgradeHandling,
            MenuTextId.UpgradeDashPower => upgradeDashPower,
            MenuTextId.UpgradeSpeedMultiplier => upgradeSpeedMultiplier,
            MenuTextId.UpgradeJumpStrength => upgradeJumpStrength,
            MenuTextId.UpgradeHackingSpeed => upgradeHackingSpeed,
            MenuTextId.UpgradeHackValue => upgradeHackValue,
            MenuTextId.UpgradeStrength => upgradeStrength,
            MenuTextId.UpgradeRange => upgradeRange,
            MenuTextId.UpgradeAccuracy => upgradeAccuracy,
            MenuTextId.Controls => controls,
            MenuTextId.ControlsSectionShip => controlsSectionShip,
            MenuTextId.ControlsSectionCar => controlsSectionCar,
            MenuTextId.ControlsSectionGeneral => controlsSectionGeneral,
            MenuTextId.PressKey => pressKey,
            MenuTextId.PressButton => pressButton,
            MenuTextId.SwappedWith => swappedWith,
            MenuTextId.RestoreDefaults => restoreDefaults,
            MenuTextId.DefaultsRestored => defaultsRestored,
            MenuTextId.NoGamepad => noGamepad,
            MenuTextId.HintRebind => hintRebind,
            MenuTextId.HintDevice => hintDevice,
            MenuTextId.ActionSteerLeft => actionSteerLeft,
            MenuTextId.ActionSteerRight => actionSteerRight,
            MenuTextId.ActionDashLeft => actionDashLeft,
            MenuTextId.ActionDashRight => actionDashRight,
            MenuTextId.ActionAccelerate => actionAccelerate,
            MenuTextId.ActionBrake => actionBrake,
            MenuTextId.ActionHandbrake => actionHandbrake,
            MenuTextId.ActionRespawn => actionRespawn,
            MenuTextId.ActionCityMap => actionCityMap,
            MenuTextId.ActionRadioPrevious => actionRadioPrevious,
            MenuTextId.ActionRadioNext => actionRadioNext,
            MenuTextId.ActionCameraCycle => actionCameraCycle,
            MenuTextId.ActionLookBack => actionLookBack,
            MenuTextId.ActionCameraLeft => actionCameraLeft,
            MenuTextId.ActionCameraRight => actionCameraRight,
            MenuTextId.ActionCameraUp => actionCameraUp,
            MenuTextId.ActionCameraDown => actionCameraDown,
            MenuTextId.MissionBrief => missionBrief,
            MenuTextId.OptionalChallenges => optionalChallenges,
            MenuTextId.Reward => reward,
            MenuTextId.Accept => accept,
            MenuTextId.ChallengeBonus => challengeBonus,
            MenuTextId.ChallengeDone => challengeDone,
            MenuTextId.ChallengeFailed => challengeFailed,
            MenuTextId.HoldToSkip => holdToSkip,
            MenuTextId.MissionComplete => missionComplete,
            MenuTextId.MainObjectives => mainObjectives,
            MenuTextId.FiniteRunObjectives => finiteRunObjectives,
            MenuTextId.Total => total,
            MenuTextId.Rank => rank,
            MenuTextId.NextMission => nextMission,
            MenuTextId.Retry => retry,
            MenuTextId.MissionBonus => missionBonus,
            MenuTextId.StatRank => statRank,
            MenuTextId.Loading => loading,
            MenuTextId.Log => log,
            MenuTextId.LogSectionGlobal => logSectionGlobal,
            MenuTextId.LogSectionVehicles => logSectionVehicles,
            MenuTextId.LogSectionLastLevel => logSectionLastLevel,
            MenuTextId.LogSectionRunner => logSectionRunner,
            MenuTextId.StatPlayTime => statPlayTime,
            MenuTextId.StatLevelsCompleted => statLevelsCompleted,
            MenuTextId.StatDeaths => statDeaths,
            MenuTextId.StatArrests => statArrests,
            MenuTextId.StatMaxSpeed => statMaxSpeed,
            MenuTextId.StatMaxJump => statMaxJump,
            MenuTextId.StatTotaledCars => statTotaledCars,
            MenuTextId.StatTotaledPolice => statTotaledPolice,
            MenuTextId.StatMoneyEarned => statMoneyEarned,
            MenuTextId.StatBonusObjectives => statBonusObjectives,
            MenuTextId.StatLevelName => statLevelName,
            MenuTextId.StatLastObjective => statLastObjective,
            MenuTextId.StatOptionalObjectives => statOptionalObjectives,
            MenuTextId.StatEscapesAttempted => statEscapesAttempted,
            MenuTextId.StatEscapesCompleted => statEscapesCompleted,
            MenuTextId.StatFastestEscape => statFastestEscape,
            MenuTextId.StatPowerUps => statPowerUps,
            MenuTextId.StatSlowDowns => statSlowDowns,
            MenuTextId.Missions => missions,
            MenuTextId.MissionLabel => missionLabel,
            MenuTextId.MissionNext => missionNext,
            MenuTextId.StartMissionTarget => startMissionTarget,
            MenuTextId.RequiresMoney => requiresMoney,
            MenuTextId.RequiresUpgrade => requiresUpgrade,
            MenuTextId.ComingSoon => comingSoon,
            MenuTextId.HintPlay => hintPlay,
            _ => start
        };
    }

}
