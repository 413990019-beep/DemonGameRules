using System;
using System.Linq;
using System.Threading.Tasks;
using ai;
using UnityEngine;
using DemonGameRules.code;
using DemonGameRules;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib; // 为 Traverse 反射取字段
using System.Reflection;
using System.Collections;


namespace DemonGameRules2.code
{
    public static partial class traitAction
    {

        private static bool isDemonMode = true;  // 默认开启恶魔模式


        // 前缀称谓字典
        private static readonly Dictionary<string, string[]> _PrefixTitlesnoneMap = new Dictionary<string, string[]>
        {
            {"PrefixTitlesnone", new string[] { "1", "2", "3" }}
        };

        // 修正后的称谓字典
        private static readonly Dictionary<string, string[]> _WarriorTitlesnoneMap = new Dictionary<string, string[]>
        {
            {"WarriorTitlesnone", new string[] { "1", "2", "3" }}
        };

        private static List<string> _availablePrefixes = new List<string>();
        private static List<string> _availableWarriors = new List<string>();
        private static HashSet<string> _usedTitles = new HashSet<string>(); // 高性能去重

        // 文件输出路径
        private static readonly string LogDirectory = Path.Combine(Application.persistentDataPath, "logs");
        private static string currentSessionFile = null;
        private static DateTime sessionStartTime;

        #region 排行榜核心静态变量
        // 击杀排行榜：Key=角色名，Value=击杀数
        public static List<KeyValuePair<string, int>> killLeaderboard = new List<KeyValuePair<string, int>>();

        // 战力排行榜：Key=角色名，Value=战力值
        public static List<KeyValuePair<string, float>> powerLeaderboard = new List<KeyValuePair<string, float>>();
        #endregion



        // ========= 恶魔飞升冠军的延时还原（仅此一位，不遍历全体） =========
        private static Actor DemonChampion = null;          // 当前飞升为恶魔的冠军（替换后的恶魔实例）
        private static int DemonRevertYear = -1;            // 目标还原年份
        private static string DemonChampionBaseName = null; // 记录冠军的 basename（仅用于判断与日志）
        private static string DemonChampionOriginalFullName = null; // 记录冠军飞升前完整名字（仅用于日志）
                                                                    // ===============================================================

        // 记录每个 baseName 的“最后一次看到的战力”和显示名（用于长榜）
        private static readonly Dictionary<string, float> _lastKnownPower = new Dictionary<string, float>();
        private static readonly Dictionary<string, string> _lastKnownDisplayName = new Dictionary<string, string>();

        // ===== 战争监控（运行时比对法） =====
        private static float _lastWarScanWorldSeconds = -999f;
        private const float WAR_SCAN_INTERVAL_SECONDS = 1.0f;
        private static readonly Dictionary<long, (Kingdom a, Kingdom b)> _warsLive = new();


        // ===== 世界切换自举 & 冷启动保护 =====
        private static MapBox _lastWorldRef = null;     // 用来判断是否换了世界实例（或读档）
        private static int _lastSeenYear = -1;          // 上一帧看到的年份
        private static bool _bootstrapped = false;      // 是否已完成本世界的自举
        private static bool _warScanPrimed = false;     // 战争监控器是否已完成首帧建快照
        private static float _graceUntilWorldSec = 10f;  // 冷启动宽限截止时间（世界秒）

        // 给周期事件统一判定“是否在冷启动宽限期”
        private static bool InGracePeriod()
        {
            var w = World.world;
            if (w == null) return true;
            float nowSec = (float)(w.getCurWorldTime() * 60.0); // getCurWorldTime() 单位分钟
            return nowSec < _graceUntilWorldSec;
        }





        #region 基础功能方法

        public static int YearNow()
        {
            try { return Date.getCurrentYear(); }  // 正确的年份入口
            catch { return 1; }
        }


        public static void TryAutoFavorite(Actor a)
        {
            if (!_autoFavoriteEnabled || a?.data == null || a.data.favorite) return;
            a.data.favorite = true;
        }






        /// <summary>
        /// 当检测到“世界实例改变”或“年份突跃”（比如从0年瞬移到1000年）时，
        /// 把所有周期事件的基准对齐到“当前年所在的周期边界”，并开一个短暂冷启动宽限期。
        /// </summary>
        private static void BootstrapOnWorldSwitchIfNeeded()
        {
            var w = World.world;
            if (w == null) { _bootstrapped = false; return; }

            int curYear = YearNow();
            bool worldChanged = !ReferenceEquals(_lastWorldRef, w);
            bool yearJumped = (_lastSeenYear >= 0 && Math.Abs(curYear - _lastSeenYear) >= 50);

            if (!_bootstrapped || worldChanged || yearJumped)
            {
                // 1) 把各周期“上次发生的年份”对齐到当前周期边界 => 不会立刻触发
                lastReportedYear = curYear - (curYear % REPORT_INTERVAL_YEARS);
                lastWarYear = curYear - (curYear % _warIntervalYears);
                lastPowerUpdateYear = curYear - (curYear % POWER_UPDATE_INTERVAL_YEARS);
                lastUnitSpawnYear = curYear; // 逐年刷新的那类，直接设为当前年

                // 2) 你的两类长周期事件
                _lastEvilRedMageYear = curYear - (curYear % EVIL_RED_MAGE_INTERVAL);
                _lastGreatContestYear = curYear - (curYear % _greatContestIntervalYears);

                // 3) 战争监控器
                _warScanPrimed = false;

                // 4) 千年长榜状态重置
                _prevYear = curYear;
                lastMillenniumExportYear = -1;

                // 5) 冷启动宽限
                float nowSec = (float)(w.getCurWorldTime() * 60.0);
                _graceUntilWorldSec = nowSec + 10f;

                _lastWorldRef = w;
                _lastSeenYear = curYear;
                _bootstrapped = true;

                UnityEngine.Debug.Log($"[DGR] Bootstrap at year {curYear}. Periods aligned; grace until {_graceUntilWorldSec:F1}s.");
                UnityEngine.Debug.Log($"[编年史] 启动保护：当前世界年={curYear}，已对齐周期事件.");

             
            }
            else
            {
                _lastSeenYear = curYear;
            }
        }


        // 释放称号的方法，供外部调用
        public static void ReleaseTitle(string title)
        {
            if (_usedTitles.Contains(title))
            {
                _usedTitles.Remove(title);
            }
        }

        public static string GetUniqueTitle()
        {
            // 组合称谓（格式：前缀·称谓）
            string fullTitle = "none";
            // 注：当前返回固定值"none"，若需实际组合逻辑，需补充前缀+称谓的随机/筛选逻辑
            return fullTitle;
        }

        /// <summary>
        /// 获取基础名字（去除头衔、死亡前缀）
        /// </summary>
        /// <summary>
        /// 获取基础名字（去除头衔、死亡前缀）
        /// </summary>
        public static string GetBaseName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return fullName;

            // 先统一去掉 [死亡]- 前缀
            fullName = TrimDeathPrefix(fullName);
            if (string.IsNullOrEmpty(fullName))
                return fullName;

            // 末个 '-' 与 ' ' 的位置
            int lastDash = fullName.LastIndexOf('-');
            int lastSpace = fullName.LastIndexOf(' ');

            // 只有 lastDash > 0 时，才合法地去找倒数第二个 '-'
            int secondLastDash = (lastDash > 0) ? fullName.LastIndexOf('-', lastDash - 1) : -1;
            // 同理，只有 lastSpace > 0 时，才去找倒数第二个空格
            int secondLastSpace = (lastSpace > 0) ? fullName.LastIndexOf(' ', lastSpace - 1) : -1;

            bool useDash = lastDash >= 0 && (lastSpace < 0 || lastDash > lastSpace);
            bool useSpace = lastSpace >= 0 && (lastDash < 0 || lastSpace > lastDash);

            if (useDash)
            {
                if (secondLastDash < 0)
                    return fullName.Substring(0, Math.Max(0, lastDash)).Trim();
                int start = secondLastDash + 1;
                int len = lastDash - start;
                if (start >= 0 && len > 0 && start + len <= fullName.Length)
                    return fullName.Substring(start, len).Trim();
                return fullName.Trim();
            }
            else if (useSpace)
            {
                if (secondLastSpace < 0)
                    return fullName.Substring(0, Math.Max(0, lastSpace)).Trim();
                int start = secondLastSpace + 1;
                int len = lastSpace - start;
                if (start >= 0 && len > 0 && start + len <= fullName.Length)
                    return fullName.Substring(start, len).Trim();
                return fullName.Trim();
            }

            return fullName.Trim();
        }

        /// <summary>
        /// 获取完整称号（去除死亡前缀，保留头衔部分）
        /// </summary>
        public static string GetFullTitle(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return fullName;

            // 先统一去掉 [死亡]- 前缀
            fullName = TrimDeathPrefix(fullName);
            if (string.IsNullOrEmpty(fullName))
                return fullName;

            int lastDash = fullName.LastIndexOf('-');
            int lastSpace = fullName.LastIndexOf(' ');

            int secondLastDash = (lastDash > 0) ? fullName.LastIndexOf('-', lastDash - 1) : -1;
            int secondLastSpace = (lastSpace > 0) ? fullName.LastIndexOf(' ', lastSpace - 1) : -1;

            bool useDash = lastDash >= 0 && (lastSpace < 0 || lastDash > lastSpace);
            bool useSpace = lastSpace >= 0 && (lastDash < 0 || lastSpace > lastDash);

            if (useDash)
            {
                if (secondLastDash < 0)
                    return fullName.Substring(0, Math.Max(0, lastDash)).Trim();
                int start = secondLastDash + 1;
                int len = lastDash - start;
                if (start >= 0 && len > 0 && start + len <= fullName.Length)
                    return fullName.Substring(start, len).Trim();
                return fullName.Trim();
            }
            else if (useSpace)
            {
                if (secondLastSpace < 0)
                    return fullName.Substring(0, Math.Max(0, lastSpace)).Trim();
                int start = secondLastSpace + 1;
                int len = lastSpace - start;
                if (start >= 0 && len > 0 && start + len <= fullName.Length)
                    return fullName.Substring(start, len).Trim();
                return fullName.Trim();
            }

            return fullName.Trim();
        }


        /// <summary>
        /// 移除名称开头的 [死亡]- 前缀（防止重复）
        /// </summary>
        public static string TrimDeathPrefix(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            while (name.StartsWith("[死亡]-"))
            {
                name = name.Substring("[死亡]-".Length);
            }
            return name;
        }

        // 统一的击杀数颜色方法
        public static string GetKillColor(int kills)
        {
            // 按击杀数分档，兼顾原公开方法（高击杀）和排行榜方法（低击杀）的颜色需求
            return kills switch
            {
                >= 3000 => "#FF4500",  // 3000杀+：橙红色（原排行榜100杀+颜色）
                >= 2000 => "#AA88FF",  // 2000-2999杀：淡紫色（原公开方法）
                >= 1000 => "#66AAFF",  // 1000-1999杀：淡蓝色（原公开方法）
                >= 300 => "#88CC88",   // 300-999杀：淡绿色（原公开方法）
                >= 100 => "#FF6347",   // 100-299杀：浅红色（原排行榜50-99杀颜色）
                >= 50 => "#32CD32",    // 50-99杀：lime绿色（原排行榜20-49杀颜色）
                >= 20 => "#00BFFF",    // 20-49杀：天蓝色（原排行榜10-19杀颜色）
                >= 10 => "#FFFFFF",    // 10-19杀：白色（原排行榜10杀以下颜色）
                _ => "#DDDDDD"         // 1-9杀：灰色（原公开方法默认色）
            };
        }

        public static string GetAttackTypeChinese(AttackType type)
        {
            return type switch
            {
                AttackType.Acid => "酸蚀 / Acid",
                AttackType.Fire => "火焰 / Fire",
                AttackType.Plague => "瘟疫 / Plague",
                AttackType.Infection => "感染 / Infection",
                AttackType.Tumor => "肿瘤 / Tumor",
                AttackType.Other => "恶魔游戏规则 / DemonGameRules",
                AttackType.Divine => "神圣 / Divine",
                AttackType.AshFever => "灰烬热 / Ash Fever",
                AttackType.Metamorphosis => "变异 / Metamorphosis",
                AttackType.Starvation => "饥饿 / Starvation",
                AttackType.Eaten => "吞噬 / Eaten",
                AttackType.Age => "衰老 / Age",
                AttackType.Weapon => "武器 / Weapon",
                AttackType.None => "未知 / None",
                AttackType.Poison => "毒素 / Poison",
                AttackType.Gravity => "地图杀 / Gravity",
                AttackType.Drowning => "溺水 / Drowning",
                AttackType.Water => "水 / Water",
                AttackType.Explosion => "爆炸 / Explosion",
                AttackType.Smile => "微笑 / Smile",
                _ => "空白 / Blank"
            };
        }

        public static string GetHealthColor(int current, int max)
        {
            if (max <= 0) return "#AAAAAA";

            float percentage = (float)current / max;
            return percentage switch
            {
                >= 1.0f => "#70DC70",      // 暗绿色
                >= 0.7f => "#70DC70",      // 柔绿色
                >= 0.4f => "#DDB700",      // 暗金色
                >= 0.2f => "#DD7E00",      // 暗橙色
                _ => "#AAAAAA"             // 白色
            };
        }
        #endregion



        #region 排行榜辅助方法
        /// <summary>
        /// 确保存活单位排在排行榜前面
        /// </summary>
        /// 
        /// 
      


        public static void EnsureLivingFirstPlace()
        {
            if (World.world?.units == null) return;

            // 获取所有存活单位
            var livingUnits = World.world.units
                .Where(actor => actor != null && actor.hasHealth() && !actor.name.StartsWith("[死亡]-"))
                .Select(actor =>
                {
                    string baseName = TrimDeathPrefix(GetBaseName(actor.name));
                    string kingdom = actor.kingdom?.name ?? "无阵营";
                    string displayName = $"{actor.name}({kingdom})";
                    int kills = actor.data?.kills ?? 0;
                    return new KeyValuePair<string, int>(displayName, kills);
                })
                .GroupBy(e => TrimDeathPrefix(GetBaseName(e.Key.Split('(')[0])))
                .Select(g => g.OrderByDescending(x => x.Value).First())
                .OrderByDescending(e => e.Value)
                .ToList();

            // 取前三名
            var top3 = livingUnits.Take(3).ToList();

            // 剩余单位（存活第4名之后 + 死亡单位）
            var remainingLiving = livingUnits.Skip(3);
            var deadUnits = killLeaderboard
                .Where(e => e.Key.StartsWith("[死亡]-"))
                .Select(e => new KeyValuePair<string, int>(e.Key, e.Value));

            var restUnits = remainingLiving.Concat(deadUnits)
                .GroupBy(e => TrimDeathPrefix(GetBaseName(e.Key.Split('(')[0])))
                .Select(g => g.OrderByDescending(x => x.Value).First())
                .OrderByDescending(e => e.Value)
                .Take(7)
                .ToList();

            // 更新排行榜前 10
            killLeaderboard = top3.Concat(restUnits).ToList();
        }

        /// <summary>
        /// 检查单位是否存活（通过显示名称判断）
        /// </summary>
        private static bool IsUnitAlive(string unitEntry)
        {
            return !unitEntry.StartsWith("[死亡]-");
        }
        #endregion

        #region 战力计算与排行榜更新
        /// <summary>
        /// 计算单位战力值（基于属性、击杀数和年龄）
        /// </summary>



        public static float CalculatePower(Actor actor)
        {
            if (actor == null || !actor.hasHealth()) return 0;

            // 基础属性
            float damage = actor.stats["damage"];
            float health = actor.stats["health"];
            float speed = actor.stats["speed"];
            float armor = actor.stats["armor"];

            // 击杀数加成
            int kills = actor.data?.kills ?? 0;
            float killBonus = kills * 0.5f;

            // 年龄加成（年龄越大经验越丰富）
            float age = (float)actor.getAge();
            float ageBonus = Mathf.Min(age * 0.1f, 10f); // 最大10点加成

            // 计算综合战力
            float power = (damage * 2) + (health * 0.5f) + (speed * 0.5f) + (armor * 1.5f) + killBonus + ageBonus;

            // ===== 平滑衰减 =====
            // decay < 1 越小衰减越狠，推荐 0.7 ~ 0.9
            const float decay = 0.8f;
            power = Mathf.Pow(power, decay);

            return Mathf.Round(power * 10f) / 10f; // 保留一位小数
        }


        /// <summary>
        /// 更新战力排行榜
        /// </summary>
        public static void UpdatePowerLeaderboard()
        {
            if (World.world?.units == null) return;

            powerLeaderboard.Clear();

            var livingUnits = World.world.units
                .Where(actor => actor != null && actor.hasHealth() && !actor.name.StartsWith("[死亡]-"))
                .Select(actor =>
                {
                    string baseName = TrimDeathPrefix(GetBaseName(actor.name));
                    string kingdom = actor.kingdom?.name ?? "无阵营";
                    string displayName = $"{actor.name}({kingdom})";
                    float power = CalculatePower(actor);
                    return new KeyValuePair<string, float>(displayName, power);
                })
                .GroupBy(e => TrimDeathPrefix(GetBaseName(e.Key.Split('(')[0])))
                .Select(g => g.OrderByDescending(x => x.Value).First())
                .OrderByDescending(e => e.Value)
                .ToList();

            powerLeaderboard = livingUnits.Take(10).ToList();

            // 同步“最后一次看到的战力” + 阈值类授勋
            foreach (var actor in World.world.units.Where(u => u != null && u.hasHealth() && !u.name.StartsWith("[死亡]-")))
            {
                try
                {
                    string baseName = TrimDeathPrefix(GetBaseName(actor.name));
                    string display = $"{actor.name}({actor.kingdom?.name ?? "无阵营"})";
                    float power = CalculatePower(actor);

                    _lastKnownPower[baseName] = power;
                    _lastKnownDisplayName[baseName] = display;

                    // —— 阈值类
                    float rp = CalculatePower(actor);
                    int age = (int)actor.getAge();

                    if (rp >= 100_000_000f && !actor.hasTrait("flesh_of_the_divine"))
                        actor.addTrait("flesh_of_the_divine");

                    if (age >= 500 && rp >= 100_000f && !actor.hasTrait("ageless"))
                        actor.addTrait("ageless");

                    if (actor.hasTrait("flesh_of_the_divine") && actor.hasTrait("incarnation_of_slaughter") && !actor.hasTrait("world_eater"))
                        actor.addTrait("world_eater");
                }
                catch { }
            }

            // —— 永恒传奇：全图战力第一且非飞升系，保证全局唯一
            try
            {
                if (World.world == null || World.world.units == null) return;

                // 先筛掉飞升系，避免选到不该拿的人
                var top = World.world.units
                    .Where(u => u != null
                                && u.hasHealth()
                                && !u.name.StartsWith("[死亡]-")
                                && !u.hasTrait("ascended_one")
                                && !u.hasTrait("ascended_demon")
                                && !u.hasTrait("daozhu"))
                    .OrderByDescending(u => CalculatePower(u))
                    .FirstOrDefault();

                // 先统一收回：把所有在世持有者的“永恒传奇”移除，稍后只给真正榜一
                foreach (var holder in World.world.units.Where(u =>
                             u != null && u.hasHealth() && u.hasTrait("eternal_legend")))
                {
                    if (holder != top) holder.removeTrait("eternal_legend");
                }

                // 若存在合法的榜一，则授予；如果没有（比如全是飞升系），那就全图没人持有
                if (top != null && !top.hasTrait("eternal_legend"))
                    top.addTrait("eternal_legend");
            }
            catch
            {
                // 别让奖杯逻辑把战斗卡死
            }

        }


        #endregion

        #region 文件输出方法
        /// <summary>
        /// 初始化新的会话文件
        /// </summary>
        public static void InitializeNewSessionFile()
        {

            if (!_txtLogEnabled) return;
            try
            {
                // 确保目录存在
                if (!Directory.Exists(LogDirectory))
                {
                    Directory.CreateDirectory(LogDirectory);
                }

                // 记录会话开始时间
                sessionStartTime = DateTime.Now;

                // 生成基于时间的文件名
                string timestamp = sessionStartTime.ToString("yyyyMMdd_HHmmss");
                currentSessionFile = Path.Combine(LogDirectory, $"leaderboard_{timestamp}.txt");

                // 创建文件并写入会话开始信息
                string sessionHeader = $"游戏会话开始时间: {sessionStartTime:yyyy-MM-dd HH:mm:ss}\n";
                sessionHeader += "=".Repeat(50) + "\n\n";

                File.WriteAllText(currentSessionFile, sessionHeader);

                Debug.Log($"新的排行榜文件已创建: {currentSessionFile}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"创建排行榜文件失败: {ex.Message}");
                currentSessionFile = null;
            }
        }

        /// <summary>
        /// 将排行榜信息写入当前会话文件
        /// </summary>
        public static void WriteLeaderboardToFile(string content)
        {
            if (!_txtLogEnabled) return;

            if (string.IsNullOrEmpty(currentSessionFile)) return;

            try
            {
                // 追加写入文件
                File.AppendAllText(currentSessionFile, content);

                // 只有在重要事件时才显示日志，减少控制台输出
                if (content.Contains("恶魔飞升仪式") || content.Contains("荣耀榜"))
                {
                    Debug.Log($"编年历信息已保存到: {currentSessionFile}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"保存编年历信息失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 生成纯文本格式的排行榜信息（不含颜色标签）
        /// </summary>
        private static string GeneratePlainTextLeaderboard(int globalLivingCount, int year)
        {

            StringBuilder sb = new StringBuilder();

            // 添加记录时间戳
            sb.AppendLine($"【世界历{year}年 - 第{(year / 50) + 1}次荣耀榜】");
            sb.AppendLine($"记录时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"【世界历{year}年】");
            sb.AppendLine($"存活单位数量: {globalLivingCount}");
            sb.AppendLine();

            // 战力排行榜
            sb.AppendLine("【战力排行榜/Power Leaderboard】");
            if (powerLeaderboard.Count > 0)
            {
                for (int i = 0; i < powerLeaderboard.Count && i < 10; i++)
                {
                    var entry = powerLeaderboard[i];
                    string ageInfo = GetUnitAgeInfo(entry.Key);
                    string cleanName = entry.Key;
                    sb.AppendLine($"{i + 1}. {cleanName} - {entry.Value}战力{ageInfo}");
                }
            }
            else
            {
                sb.AppendLine("暂无上榜者/No rankers yet");
            }

            sb.AppendLine();

            // 杀戮排行榜
            sb.AppendLine("【杀戮排行榜/Kills Leaderboard】");
            if (killLeaderboard.Count > 0)
            {
                for (int i = 0; i < killLeaderboard.Count && i < 10; i++)
                {
                    var entry = killLeaderboard[i];
                    string ageInfo = GetUnitAgeInfo(entry.Key);
                    string cleanName = entry.Key;
                    sb.AppendLine($"{i + 1}. {cleanName} - {entry.Value}杀{ageInfo}");
                }
            }
            else
            {
                sb.AppendLine("暂无上榜者/No rankers yet");
            }

            sb.AppendLine("=".Repeat(50) + "\n");

            return sb.ToString();
        }

        /// <summary>
        /// 字符串重复扩展方法
        /// </summary>
        private static string Repeat(this string str, int count)
        {
            if (string.IsNullOrEmpty(str) || count <= 0) return "";

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                sb.Append(str);
            }
            return sb.ToString();
        }
        #endregion

        #region 排行榜显示逻辑
        /// <summary>
        /// 获取单位的年龄信息
        /// </summary>
        public static void TryMarkFavoriteByPower(Actor a)
        {
            if (a?.data == null) return;

            // 开关未开启：完全不改动收藏状态（既不加也不撤）
            if (!_autoFavoriteEnabled) return;

            float p = CalculatePower(a);

            if (p > 100000000f && IsOnAnyBoard(a))
            {
                if (!a.data.favorite)
                    a.data.favorite = true;
            }
            else
            {


            }
        }





        /// 

        private static string GetUnitAgeInfo(string displayName)
        {
            try
            {
                if (string.IsNullOrEmpty(displayName)) return "";

                string baseName = displayName;
                string kingdom = "无阵营";

                if (displayName.Contains("(") && displayName.Contains(")"))
                {
                    int parenIndex = displayName.LastIndexOf('(');
                    int closingParenIndex = displayName.LastIndexOf(')');
                    if (parenIndex >= 0 && closingParenIndex > parenIndex && closingParenIndex < displayName.Length)
                    {
                        baseName = displayName.Substring(0, parenIndex).Trim();
                        if (parenIndex + 1 < displayName.Length && closingParenIndex - parenIndex - 1 > 0)
                        {
                            kingdom = displayName.Substring(parenIndex + 1, closingParenIndex - parenIndex - 1);
                        }
                    }
                }

                baseName = TrimDeathPrefix(baseName);
                if (string.IsNullOrEmpty(baseName)) return "";

                var matchedUnit = World.world?.units?.FirstOrDefault(u =>
                    u != null && u.hasHealth() && u.name != null &&
                    (u.name.Equals(baseName, StringComparison.OrdinalIgnoreCase) || u.name.Contains(baseName))
                );



                if (matchedUnit != null)
                {
                    // 转换年龄为整数，去掉小数部分
                    int age = (int)matchedUnit.getAge();
                    return $" ( {age}岁)";  // 只显示整数部分
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"获取年龄信息失败: {ex.Message}");
            }

            return "";
        }

        public static void ShowSimplifiedLeaderboard()
        {
            try
            {
                if (string.IsNullOrEmpty(currentSessionFile))
                {
                    var slot = string.IsNullOrWhiteSpace(_saveSlotName) ? "slot" : _saveSlotName;
                    BeginNewGameSession(slot);
                }

                int globalLivingCount = World.world?.units?.Count(actor =>
                    actor != null && actor.data != null && actor.data.id > 0 && actor.hasHealth()
                ) ?? 0;

                EnsureLivingFirstPlace();
                UpdatePowerLeaderboard();

                int year = 1;
                if (World.world != null)
                {
                    year = YearNow();
                }

                string leaderboardTextStr = $"[世界历{year}年 - 第{(year / REPORT_INTERVAL_YEARS) + 1}次榜单]\n";

                leaderboardTextStr += "<color=#FF9900><b>【战力排行榜/Power Leaderboard】</b></color>\n";

                if (powerLeaderboard != null && powerLeaderboard.Count > 0)
                {
                    for (int i = 0; i < Mathf.Min(powerLeaderboard.Count, 10); i++)
                    {
                        try
                        {
                            var entry = powerLeaderboard[i];
                            if (entry.Key == null) continue;

                            string rankColor;
                            if (i == 0) rankColor = "#FFD700";
                            else if (i == 1) rankColor = "#00CED1";
                            else if (i == 2) rankColor = "#CD7F32";
                            else rankColor = "#DDDDDD";

                            string ageInfo = GetUnitAgeInfo(entry.Key);

                            leaderboardTextStr += $"{i + 1}. <color={rankColor}>{entry.Key}</color> - " +
                                                $"<color=#FFCC00>{entry.Value}</color>" +
                                                $"<color=#AAAAAA>{ageInfo}</color>\n";
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"处理战力榜第{i + 1}位时出错: {ex.Message}");
                            continue;
                        }
                    }
                }
                else
                {
                    leaderboardTextStr += "<color=#AAAAAA>暂无上榜者/No rankers yet</color>\n";
                }

                leaderboardTextStr += "<color=#666666>────────────────────</color>\n";

                leaderboardTextStr += "<color=#66AAFF><b>【杀戮排行榜/Kills Leaderboard】</b></color>\n";

                if (killLeaderboard != null && killLeaderboard.Count > 0)
                {
                    for (int i = 0; i < Mathf.Min(killLeaderboard.Count, 10); i++)
                    {
                        try
                        {
                            var entry = killLeaderboard[i];
                            if (entry.Key == null) continue;

                            string rankColor;
                            if (i == 0) rankColor = "#FFD700";
                            else if (i == 1) rankColor = "#00CED1";
                            else if (i == 2) rankColor = "#CD7F32";
                            else rankColor = IsUnitAlive(entry.Key) ? "#DDDDDD" : "#AAAAAA";

                            string ageInfo = GetUnitAgeInfo(entry.Key);

                            leaderboardTextStr += $"{i + 1}. <color={rankColor}>{entry.Key}</color> - " +
                                                $"<color={GetKillColor(entry.Value)}>{entry.Value}</color>" +
                                                $"<color=#AAAAAA>{ageInfo}</color>\n";
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"处理击杀榜第{i + 1}位时出错: {ex.Message}");
                            continue;
                        }
                    }
                }
                else
                {
                    leaderboardTextStr += "<color=#AAAAAA>暂无上榜者/No rankers yet</color>\n";
                }

                NotificationHelper.ShowThisMessage(leaderboardTextStr);

                Debug.Log(
                    $"\n<color=#666666>════════════════════════════════════════════</color>\n" +
                    $"<color=#FFFF99>☻：{globalLivingCount}</color>\n" +
                    $"{leaderboardTextStr}" +
                    $"<color=#666666>════════════════════════════════════════════</color>\n"
                );

                string plainTextContent = GeneratePlainTextLeaderboard(globalLivingCount, year);
                WriteLeaderboardToFile(plainTextContent);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[简化排行榜异常]：{ex.Message}");
            }
        }


        private static void ExportMillenniumLongBoard(int worldYear)
        {
            if (World.world?.units == null) return;

            var merged = new Dictionary<string, KeyValuePair<string, float>>();

            foreach (var u in World.world.units)
            {
                if (u == null || !u.hasHealth() || u.data == null) continue;
                string baseName = TrimDeathPrefix(GetBaseName(u.name));
                float power = CalculatePower(u);
                if (power > 1000f)
                {
                    string display = $"{u.name}({u.kingdom?.name ?? "无阵营"})";
                    merged[baseName] = new KeyValuePair<string, float>(display, power);
                }
            }

            foreach (var kv in _lastKnownPower)
            {
                string baseName = kv.Key;
                float power = kv.Value;
                if (power <= 1000f) continue;

                string display = _lastKnownDisplayName.TryGetValue(baseName, out var dn) ? dn : baseName;
                if (!merged.TryGetValue(baseName, out var exist) || power > exist.Value)
                {
                    merged[baseName] = new KeyValuePair<string, float>(display, power);
                }
            }

            // 生成千年长榜并限制最多50个条目
            var sb = new StringBuilder();
            sb.AppendLine($"【世界历{worldYear}年】千年长榜（战力＞1000，死活同列）"); // 文案改为“＞”
            var top50Entries = merged.Values
                                      .OrderByDescending(v => v.Value)           // 按战力排序
                                      .ThenBy(v => v.Key, StringComparer.Ordinal) // 再按名称排序
                                      .Take(50);                               // 仅取前50条记录

            foreach (var e in top50Entries)
            {
                sb.AppendLine($"{e.Key} - {e.Value:F1}"); // 一位小数
            }

            WriteLeaderboardToFile(sb.ToString());
        }




        // 放在 traitAction 类里
        private static bool IsOnAnyBoard(Actor a)
        {
            if (a == null || a.data == null || string.IsNullOrEmpty(a.name)) return false;
            string baseName = TrimDeathPrefix(GetBaseName(a.name));

            bool onKills = killLeaderboard.Any(e =>
            {
                string nameNoKingdom = e.Key.Split('(')[0].Trim();
                string bn = TrimDeathPrefix(GetBaseName(nameNoKingdom));
                return string.Equals(bn, baseName, StringComparison.OrdinalIgnoreCase);
            });

            bool onPower = powerLeaderboard.Any(e =>
            {
                string nameNoKingdom = e.Key.Split('(')[0].Trim();
                string bn = TrimDeathPrefix(GetBaseName(nameNoKingdom));
                return string.Equals(bn, baseName, StringComparison.OrdinalIgnoreCase);
            });

            return onKills || onPower;
        }


        #endregion

        #region 计时器逻辑



        private static int _warIntervalYears = 100; // 默认30，可被 OnWarIntervalChanged 修改
        private static int lastWarYear = 0;

        private static int lastUnitSpawnYear = 0; // 新增：跟踪上次生成单位的年份
        private static int lastPowerUpdateYear = 0; // 新增：跟踪上次更新战力排行榜的年份
        private const int POWER_UPDATE_INTERVAL_YEARS = 10; // 新增：战力排行榜更新间隔（10年）
        private static int lastReportedYear = 0;
        private const int REPORT_INTERVAL_YEARS = 50;

        // 每777年触发一次：在任意城市/山地生成1名恶魔使徒，并赋予5000击杀与若干战斗特质。
        private const int EVIL_RED_MAGE_INTERVAL = 777;     // 事件间隔：777年
        private const int EVIL_RED_MAGE_SURVIVE_YEARS = 77; // 存活满 77 年则自动还原
        private static int _lastEvilRedMageYear = 0;

        private static Actor EvilRedMage = null;            // 当前场上的恶魔使徒
        private static int EvilRedMageSpawnYear = -1;       // 生成年份
        private static int EvilRedMageRevertYear = -1;      // 目标还原年份（生成年 + 88）
        private static string EvilRedMageOriginalName = null; // 生成时的原名（用于日志）
        private const string EVIL_RED_MAGE_FIXED_NAME = "恶魔使徒";

        private static int lastMillenniumExportYear = -1; // 最近一次导出长榜的年份
        private static int _prevYear = -1;

        public static void UpdateLeaderboardTimer()
        {
            if (World.world == null) return;

            // —— 世界切换自举；必须放最前（会重置 _prevYear / lastMillenniumExportYear）
            BootstrapOnWorldSwitchIfNeeded();

            // —— 战争监控每帧先跑，捕获“结束/开战”
            ScanWarDiffs();

            int currentYear = YearNow();

            // 冷启动宽限：UI/榜单输出允许，推进型事件冻结
            bool freezeProgressiveEvents = InGracePeriod();

            

            // 每年刷一点随机单位
            if (!freezeProgressiveEvents && currentYear > lastUnitSpawnYear)
            {
                SpawnRandomUnit();
                lastUnitSpawnYear = currentYear;
            }

            // 10 年一次更新战力榜
            if (currentYear >= lastPowerUpdateYear + POWER_UPDATE_INTERVAL_YEARS)
            {
                UpdatePowerLeaderboard();
                lastPowerUpdateYear = currentYear - (currentYear % POWER_UPDATE_INTERVAL_YEARS);
            }

            // 30 年一次的强制宣战/叛乱
            if (!freezeProgressiveEvents && currentYear >= lastWarYear + _warIntervalYears)
            {
                TryRandomWar();
                lastWarYear = currentYear - (currentYear % _warIntervalYears);
            }

            if (!freezeProgressiveEvents)
            {
                CheckEvilRedMageEvent(currentYear);
            }

            if (isDemonMode)
            {
                

            }
            else
            {
            
            }
            // 大道争锋 / 恶魔使徒（推进型，冷启动期内冻结）
            if (!freezeProgressiveEvents)
            {
                CheckGreatContest();
   
            }

            if (IsGreatContestActive) EnforceContestantMadness();

            // 还原检查（非推进型，允许）
            CheckEvilRedMageRevert(currentYear);
            CheckChampionRevert(currentYear);

            // 50 年一次榜单展示（允许）
            if (currentYear >= lastReportedYear + REPORT_INTERVAL_YEARS)
            {
                ShowSimplifiedLeaderboard();
                lastReportedYear = currentYear - (currentYear % REPORT_INTERVAL_YEARS);
            }

            // —— 千年长榜导出（只在自然跨到整千年的“那一帧”触发；不补写）
            // 1) 必须不是冷启动冻结期
            // 2) “自然+1年”跨过来的那一帧（current == prev + 1）
            // 3) currentYear 正好是 1000 的整数倍
            // 4) 本年尚未导出过（lastMillenniumExportYear != currentYear）
            if (!freezeProgressiveEvents
                && _prevYear >= 0
                && currentYear == _prevYear + 1
                && (currentYear % 1000) == 0
                && lastMillenniumExportYear != currentYear)
            {
                ExportMillenniumLongBoard(currentYear);
                lastMillenniumExportYear = currentYear; // 标记已导出，避免同年重复
            }


            // 更新年快照（放最后）
            _prevYear = currentYear;
        }


        #endregion



        #region 大道争锋机制 (Great Contest)
        public static bool IsGreatContestActive = false;
        public static int GreatContestStartYear = 0;
        public static List<Actor> GreatContestants = new List<Actor>();
        public static City GreatContestArenaCity = null;
        private static int _lastGreatContestYear = -960;

        //private const int GREAT_CONTEST_INTERVAL = 480;
        private static int _greatContestIntervalYears = 480;
        private const int GREAT_CONTEST_DURATION = 10;
        private const int MIN_CONTESTANTS = 5;
        private const int MAX_CONTESTANTS = 10;
        private const int FINAL_SURVIVORS = 3;

        private const int MADNESS_CHECK_FRAME_INTERVAL = 15;

        // CHANGED: 删除称号数组与逻辑，改为授予特质
        private const string TRAIT_ASCENDED_DEMON = "ascended_demon";
        private const string TRAIT_DAOZHU = "daozhu";

        public static void CheckGreatContest()
        {
            if (World.world == null) return;

            int currentYear = YearNow();

            if (!IsGreatContestActive && currentYear >= _lastGreatContestYear + _greatContestIntervalYears)
            {
                StartGreatContest(currentYear);
            }

            if (IsGreatContestActive)
            {
                CheckGreatContestProgress(currentYear);
            }
        }

        private static void StartGreatContest(int currentYear)
        {
            try
            {
                var potentialContestants = World.world.units
                    .Where(actor => actor != null &&
                                actor.hasHealth() &&
                                !actor.name.StartsWith("[死亡]-") &&
                                CalculatePower(actor) > 50)
                    .OrderByDescending(actor => CalculatePower(actor))
                    .Take(MAX_CONTESTANTS)
                    .ToList();

                string log = "";
                if (potentialContestants.Count < MIN_CONTESTANTS)
                {
                    if (isDemonMode)
                    {
                        log = $"恶魔飞升仪式取消：符合条件的候选人不足（需要{MIN_CONTESTANTS}，仅有{potentialContestants.Count}）";
                    }
                    else
                    {
                        log = $"大道争锋取消：符合条件的修士不足（需要{MIN_CONTESTANTS}，仅有{potentialContestants.Count}）";
                    }
                    Debug.Log(log);
                    WriteLeaderboardToFile($"[世界历{currentYear}年] {log}\n");
                    _lastGreatContestYear = currentYear - (currentYear % _greatContestIntervalYears);
                    return;
                }

                var cities = World.world.cities.Where(c => c != null && c.zones.Any()).ToList();
                if (!cities.Any())
                {
                    if (isDemonMode)
                    {
                        Debug.Log("恶魔飞升仪式取消：没有合适的诅咒之地作为祭坛");
                    }
                    else
                    {
                        Debug.Log("大道争锋事件取消：没有合适的城市作为竞技场");
                    }
                    return;
                }

                GreatContestArenaCity = cities[UnityEngine.Random.Range(0, cities.Count)];
                var arenaZone = GreatContestArenaCity.zones.ToArray().GetRandom<TileZone>();
                var arenaTile = arenaZone.tiles.ToArray().GetRandom<WorldTile>();

                IsGreatContestActive = true;
                GreatContestStartYear = currentYear;
                GreatContestants = potentialContestants.Take(Mathf.Min(potentialContestants.Count, MAX_CONTESTANTS)).ToList();
                _lastGreatContestYear = currentYear - (currentYear % _greatContestIntervalYears);

                int contestNumber = (currentYear / _greatContestIntervalYears) + 1;

                string contestStartLog = "";
                if (isDemonMode)
                {
                    contestStartLog = $"\n【第{contestNumber}次恶魔飞升仪式 - 世界历{currentYear}年】\n";
                    contestStartLog += $"深渊裂缝开启，魔气滔天！{GreatContestArenaCity.name}已被邪能笼罩，\n";
                    contestStartLog += $"{GreatContestants.Count}名被选中的恶魔候选人将在此地死战！唯有最强者可被邪能承认。\n\n"; // CHANGED: 叙述收敛到“特质”奖励
                    contestStartLog += $"【祭坛所在地】：{GreatContestArenaCity.name}\n";
                    contestStartLog += $"【候选人数】：{GreatContestants.Count}人\n";
                    contestStartLog += $"【仪式持续时间】：{GREAT_CONTEST_DURATION}年\n\n";
                    contestStartLog += "【候选人名单】（按战力排序）：\n";
                }
                else
                {
                    contestStartLog = $"\n【第{contestNumber}次大道争锋 - 世界历{currentYear}年】\n";
                    contestStartLog += $"天地异变，灵气汇聚！{GreatContestArenaCity.name}上空浮现神秘符文，\n";
                    contestStartLog += $"世间{GreatContestants.Count}位强者将于此争夺无上机缘。\n\n"; // CHANGED
                    contestStartLog += $"【竞技场】：{GreatContestArenaCity.name}\n";
                    contestStartLog += $"【参赛强者】：{GreatContestants.Count}人\n";
                    contestStartLog += $"【持续时间】：{GREAT_CONTEST_DURATION}年\n\n";
                    contestStartLog += "【参赛者名单】（按战力排序）：\n";
                }

                var sortedContestants = GreatContestants.OrderByDescending(c => CalculatePower(c)).ToList();
                for (int i = 0; i < sortedContestants.Count; i++)
                {
                    var contestant = sortedContestants[i];
                    string kingdom = contestant.kingdom?.name ?? "无阵营";
                    contestStartLog += $"{i + 1}. {contestant.name}({kingdom}) - {CalculatePower(contestant)}战力\n";
                }

                contestStartLog += "=".Repeat(50) + "\n\n";

                Debug.Log(contestStartLog);
                WriteLeaderboardToFile(contestStartLog);


                        // 入选即授“天命候选人”
                foreach (var c in GreatContestants)
                {
                    try { if (!c.hasTrait("chosen_of_providence")) c.addTrait("chosen_of_providence"); } catch {}
                    TryAutoFavorite(c);
                }

                
                foreach (var contestant in GreatContestants)
                {
                    try
                    {
                        ActionLibrary.teleportEffect(contestant, arenaTile);
                        contestant.cancelAllBeh();
                        contestant.setCurrentTilePosition(arenaTile);

                        if (contestant.hasTrait("strong_minded"))
                        {
                            contestant.removeTrait("strong_minded");
                        }
                        if (contestant.hasTrait("peaceful") || contestant.hasTrait("pacifist"))
                        {
                            contestant.removeTrait("peaceful");
                            contestant.removeTrait("pacifist");
                        }
                        contestant.addTrait("aggressive");
                        contestant.addTrait("bloodlust");
                        contestant.addTrait("madness");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"设置参赛者 {contestant.name} 时出错: {ex.Message}");
                    }
                }

                string notificationText = "";
                if (isDemonMode)
                {
                    notificationText = $"<color=#FFD700>【第{contestNumber}次恶魔飞升仪式开启】</color>\n" +
                                      $"<color=#FFFFFF>世界历{currentYear}年，{GreatContestants.Count}名恶魔候选人</color>\n" +
                                      $"<color=#FFCC00>汇聚于{GreatContestArenaCity.name}，强者将获赐深渊印记！</color>"; // CHANGED
                }
                else
                {
                    notificationText = $"<color=#FFD700>【第{contestNumber}次大道争锋开启】</color>\n" +
                                      $"<color=#FFFFFF>世界历{currentYear}年，{GreatContestants.Count}名强者</color>\n" +
                                      $"<color=#FFCC00>汇聚于{GreatContestArenaCity.name}，夺取道途机缘！</color>"; // CHANGED
                }
                NotificationHelper.ShowThisMessage(notificationText);

                LockContestCamera();
            }
            catch (Exception ex)
            {
                IsGreatContestActive = false;
            }
        }

        private static int _lastReportedSurvivorCount = -1;

        private static void CheckGreatContestProgress(int currentYear)
        {
            try
            {
                if (currentYear >= GreatContestStartYear + GREAT_CONTEST_DURATION)
                {
                    EndGreatContest("仪式时限已到"); // 文案在 EndGreatContest 内已区分
                    return;
                }

                var survivors = new List<Actor>();
                foreach (var contestant in GreatContestants)
                {
                    try
                    {
                        if (contestant != null && !contestant.isRekt() && contestant.hasHealth())
                        {
                            survivors.Add(contestant);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"检查候选人状态时出错: {ex.Message}");
                    }
                }

                if (survivors.Count != _lastReportedSurvivorCount)
                {
                    if (survivors.Count <= GreatContestants.Count / 2 && survivors.Count > FINAL_SURVIVORS)
                    {
                        string progressLog = isDemonMode
                            ? $"[世界历{currentYear}年] 恶魔飞升仪式进入白热化，剩余{survivors.Count}人\n"
                            : $"[世界历{currentYear}年] 大道争锋过半，剩余{survivors.Count}人\n";
                        WriteLeaderboardToFile(progressLog);
                    }
                    _lastReportedSurvivorCount = survivors.Count;
                }

                if (survivors.Count <= FINAL_SURVIVORS)
                {
                    EndGreatContest($"剩余{survivors.Count}人");
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"出错: {ex.Message}");
                EndGreatContest("系统错误");
            }
        }

        private static void EndGreatContest(string reason)
        {
            try
            {
                int currentYear = YearNow();
                int contestNumber = (currentYear / _greatContestIntervalYears) + 1;

                var survivors = new List<Actor>();
                foreach (var contestant in GreatContestants)
                {
                    try
                    {
                        if (contestant != null && !contestant.isRekt() && contestant.hasHealth())
                        {
                            survivors.Add(contestant);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"检查候选人状态时出错: {ex.Message}");
                    }
                }

                // 清理战斗状态
                foreach (var contestant in GreatContestants)
                {
                    if (contestant != null && contestant.hasTrait("madness"))
                    {
                        contestant.removeTrait("madness");
                    }
                }

                string eventEndLog = isDemonMode
                    ? $"\n【第{contestNumber}次恶魔飞升仪式终结 - 世界历{currentYear}年】\n"
                    : $"\n【第{contestNumber}次大道争锋终结 - 世界历{currentYear}年】\n";

                if (survivors.Any())
                {
                    eventEndLog += isDemonMode
                        ? $"经过{GREAT_CONTEST_DURATION}年的血腥厮杀，仪式终结。\n"
                        : $"经过{GREAT_CONTEST_DURATION}年的激烈争夺，盛会落幕。\n";
                    eventEndLog += $"最终，{survivors.Count}位幸存者见证结果。\n\n";
                }
                else
                {
                    eventEndLog += isDemonMode
                        ? $"惨烈血战后无人生还。深渊之门关闭，{_greatContestIntervalYears}年后再启。\n\n"
                        : $"无人幸存。天地静默，等待下一轮回的强者。\n\n";
                }

                eventEndLog += $"终结原因：{reason}\n";
                eventEndLog += $"最终幸存者：{survivors.Count}人\n";

                if (survivors.Any())
                {
                    var sortedSurvivors = survivors.OrderByDescending(s => CalculatePower(s)).ToList();
                    var champion = sortedSurvivors.First();

                    eventEndLog += "【最终幸存者名单】：\n";
                    for (int i = 0; i < sortedSurvivors.Count; i++)
                    {
                        var survivor = sortedSurvivors[i];
                        string kingdom = survivor.kingdom?.name ?? "无阵营";
                        eventEndLog += $"{i + 1}. {survivor.name}({kingdom}) - {CalculatePower(survivor)}战力\n";
                    }

                    // CHANGED: 授予特质，不改名不加称号
                    if (isDemonMode)
                    {
                        if (!champion.hasTrait(TRAIT_ASCENDED_DEMON))
                            champion.addTrait(TRAIT_ASCENDED_DEMON);
                        if (!champion.hasTrait("ascended_one")) champion.addTrait("ascended_one");

                        eventEndLog += $"\n<color=#FFD700>【飞升者诞生】</color> {champion.name} 获得深渊印记：<color=#FF5555>飞升恶魔</color>。\n";
                        eventEndLog += $"邪能灌体，新的恶魔领主现世！\n";
                        TurnIntoDemonAndRevert(champion, null); // 若你希望仍触发恶魔化，这里保留
                    }
                    else
                    {
                        if (!champion.hasTrait(TRAIT_DAOZHU))
                            champion.addTrait(TRAIT_DAOZHU);
                        if (!champion.hasTrait("ascended_one")) champion.addTrait("ascended_one");
                        TryAutoFavorite(champion);
                        eventEndLog += $"\n<color=#FFD700>【大道得主】</color> {champion.name} 获授机缘：<color=#FFCC00>道主</color>。\n";
                        eventEndLog += $"万物共鸣，新传奇起航！\n";
                    }
                }
                else
                {
                    eventEndLog += isDemonMode
                        ? "无人生还，恶魔宝座空悬。\n"
                        : "无人生还，大道空悬。\n";
                }

                eventEndLog += "=".Repeat(60) + "\n\n";

                Debug.Log(eventEndLog);
                WriteLeaderboardToFile(eventEndLog);

                string resultText = "";
                if (isDemonMode)
                {
                    resultText = survivors.Any()
                        ? $"<color=#FFD700>【第{contestNumber}次恶魔飞升仪式终结】</color>\n<color=#FFFFFF>{survivors.Count}名候选者幸存</color>\n<color=#AAAAAA>世界历{currentYear}年</color>"
                        : $"<color=#FF6666>【第{contestNumber}次恶魔飞升仪式终结】</color>\n<color=#FFFFFF>无人生还，恶魔宝座空悬！</color>\n<color=#AAAAAA>世界历{currentYear}年</color>";
                }
                else
                {
                    resultText = survivors.Any()
                        ? $"<color=#FFD700>【第{contestNumber}次大道争锋终结】</color>\n<color=#FFFFFF>{survivors.Count}名强者幸存</color>\n<color=#AAAAAA>世界历{currentYear}年</color>"
                        : $"<color=#FF6666>【第{contestNumber}次大道争锋终结】</color>\n<color=#FFFFFF>无人生还，大道空悬！</color>\n<color=#AAAAAA>世界历{currentYear}年</color>";
                }
                NotificationHelper.ShowThisMessage(resultText);
            }
            catch (Exception ex)
            {
                Debug.LogError($"事件结束处理失败: {ex.Message}");
            }
            finally
            {
                IsGreatContestActive = false;
                GreatContestants.Clear();
                GreatContestArenaCity = null;
                UnlockContestCamera();
            }
        }

        // CHANGED: 移除 GetUniqueTitleForChampion 与称号数组，彻底不用称号
        #endregion


        #region   保持疯狂
        private static void EnforceContestantMadness()
        {
            if (!IsGreatContestActive || World.world == null || GreatContestants == null || GreatContestants.Count == 0)
                return;

            if (Time.frameCount % MADNESS_CHECK_FRAME_INTERVAL != 0)
                return;

            foreach (var contestant in GreatContestants)
            {
                try
                {
                    if (contestant == null || contestant.isRekt() || !contestant.hasHealth())
                        continue;

                    if (contestant.hasTrait("strong_minded"))
                    {
                        contestant.removeTrait("strong_minded");
                    }
                    if (!contestant.hasTrait("madness"))
                    {
                        contestant.addTrait("madness");
                    }
                }
                catch (Exception)
                {
                }
            }
        }
        #endregion

        #region  恶魔人类随地大小变（仅冠军需要还原）
        public static bool TurnIntoDemonAndRevert(BaseSimObject pTarget, WorldTile pTile = null)
        {
            Actor pActor = pTarget.a;
            if (pActor == null) return false;
            if (!pActor.inMapBorder()) return false;
            if (pActor.isAlreadyTransformed()) return false;

            Actor tDemon = World.world.units.createNewUnit("evil_mage", pActor.current_tile,
                false, 0f, null, null, false, false, false);

            ActorTool.copyUnitToOtherUnit(pActor, tDemon, true);
            
            EffectsLibrary.spawn("fx_spawn", tDemon.current_tile, null, null, 0f, -1f, -1f, null);

            ActionLibrary.removeUnit(pActor);

            tDemon.setTransformed();

            int currentYear = YearNow();
            int revertYear = currentYear + 30;

            DemonChampion = tDemon;
            DemonRevertYear = revertYear;
            if (DemonChampionBaseName == null && !string.IsNullOrEmpty(DemonChampionOriginalFullName))
            {
                DemonChampionBaseName = GetBaseName(DemonChampionOriginalFullName);
            }
            tDemon.restoreHealth(9999999); // ← 一刀拉满，别管上限
            TryAutoFavorite(tDemon);
            string msg1 = $"[恶魔飞升] 世界历 {currentYear} 年：{(tDemon?.name ?? "未知单位")} 已变为恶魔，将在 {revertYear} 年得道成人。";
            Debug.Log(msg1);
            WriteLeaderboardToFile(msg1 + Environment.NewLine);

            return true;
        }

        public static bool TurnIntoHuman(BaseSimObject pTarget, WorldTile pTile = null)
        {
            Actor pActor = pTarget.a;
            if (pActor == null) return false;
            if (!pActor.inMapBorder()) return false;

            Actor tHuman = World.world.units.createNewUnit(
                "human",
                pActor.current_tile,
                false,
                0f,
                null,
                null,
                false,
                false,
                false
            );

            ActorTool.copyUnitToOtherUnit(pActor, tHuman, true);
            
                                           // 移除疯狂 trait
            tHuman.removeTrait("madness");
            EffectsLibrary.spawn("fx_spawn", tHuman.current_tile, null, null, 0f, -1f, -1f, null);
            ActionLibrary.removeUnit(pActor);

            // 如果你希望还原后的人类名字恢复为飞升前的完整名字，可解开下面两行：
            // if (!string.IsNullOrEmpty(DemonChampionOriginalFullName))
            //     tHuman.name = DemonChampionOriginalFullName;
            tHuman.restoreHealth(9999999); // ← 同样处理
            TryAutoFavorite(tHuman);
            tHuman.setTransformed();
            return true;
        }

        private static void CheckChampionRevert(int yearNow)
        {
            if (DemonChampion == null) return;

            if (DemonChampion.isRekt() || !DemonChampion.hasHealth())
            {
                Debug.Log("[恶魔洗礼] 仪式失败，大恶魔已不存在或死亡。");
                DemonChampion = null;
                DemonRevertYear = -1;
                return;
            }

            if (DemonRevertYear > 0 && yearNow >= DemonRevertYear)
            {
                string demonName = DemonChampion.name ?? "未知恶魔";
                string currentBase = GetBaseName(demonName);
                if (!string.IsNullOrEmpty(DemonChampionBaseName))
                {
                    bool match = currentBase.Contains(DemonChampionBaseName) || DemonChampionBaseName.Contains(currentBase);
                    //Debug.Log($"[恶魔洗礼] 校验 basename: 原={DemonChampionBaseName} / 现={currentBase} / 匹配={match}");
                }

                TurnIntoHuman(DemonChampion, null);

                string msg2 = $"[恶魔洗礼] 世界历 {yearNow} 年：{(demonName ?? "未知单位")} 已得道成人。";
                Debug.Log(msg2);
                WriteLeaderboardToFile(msg2 + Environment.NewLine);

                DemonChampion = null;
                DemonRevertYear = -1;
                DemonChampionBaseName = null;
                DemonChampionOriginalFullName = null;
            }
        }
        #endregion

        #region 恶魔使徒事件

        /// <summary>
        /// 每888年触发一次：在任意城市/山地生成1名恶魔使徒，并赋予5000击杀与若干战斗特质。
        /// </summary>
        private static void CheckEvilRedMageEvent(int currentYear)
        {
            // 已有一个在场，等待它的生死/还原，不再重复生成
            if (EvilRedMage != null && !EvilRedMage.isRekt() && EvilRedMage.hasHealth())
                return;

            // 间隔到期（整除对齐，和你其它事件逻辑一致）
            if (currentYear < _lastEvilRedMageYear + EVIL_RED_MAGE_INTERVAL)
                return;

            // 记录对齐到最近的整间隔起点（便于下一次触发计算）
            _lastEvilRedMageYear = currentYear - (currentYear % EVIL_RED_MAGE_INTERVAL);

            // 寻找落点：优先城市；没有就尝试丘陵；再不行就随机地图点
            WorldTile spawnTile = FindAnyGoodSpawnTile();
            if (spawnTile == null)
            {
                Debug.LogWarning("[恶魔使徒] 未找到合适落点，事件跳过。");
                return;
            }

            // 用接近“邪法师”视觉的单位：优先 necromancer（现有资产），并以名称+特质标明“红袍”
            // 如你另有红袍法师的具体单位 ID，可直接替换 "necromancer"
            Actor mage = World.world.units.createNewUnit(
                "evil_mage",  // 可替换为你自定义的红袍法师 id
                spawnTile,
                false,
                0f,
                null,
                null,
                false,
                false,
                false
            );

            if (mage == null)
            {
                Debug.LogWarning("[恶魔使徒] 生成失败：createNewUnit 返回 null。");
                return;
            }
            TryAutoFavorite(mage);
            // 基础入场特效
            EffectsLibrary.spawn("fx_spawn", mage.current_tile, null, null, 0f, -1f, -1f, null);


            EvilRedMageOriginalName = mage.name; // 可留作日志
            mage.data.setName(EVIL_RED_MAGE_FIXED_NAME);

            // 赋予击杀数 = 5000（直写 data.kills）
            if (mage.data != null)
            {
                mage.data.kills = 5000;
            }

            // 强化作战特质（去和平、加嗜血疯狂等）
            try
            {
                if (mage.hasTrait("strong_minded")) mage.removeTrait("strong_minded");
                if (mage.hasTrait("peaceful")) mage.removeTrait("peaceful");
                if (mage.hasTrait("pacifist")) mage.removeTrait("pacifist");

                mage.addTrait("aggressive");
                mage.addTrait("bloodlust");
                mage.addTrait("madness");
                mage.addTrait("evil"); // “邪恶”标签（若库内有该 trait）
            }
            catch { /* 忽略特质异常，尽量不中断 */ }

            // 记录全局状态
            EvilRedMage = mage;
            EvilRedMageSpawnYear = currentYear;
            EvilRedMageRevertYear = currentYear + EVIL_RED_MAGE_SURVIVE_YEARS;

            // 日志 & 文本
            string where =
                (spawnTile.zone != null && spawnTile.zone.city != null)
                ? spawnTile.zone.city.data.name
                : "荒野";

            string log =
                $"[恶魔使徒] 世界历 {currentYear} 年：一名<恶魔使徒>降临于 {where}！" +
                $" 已赋予 <5000> 击杀数，若存活至 {EvilRedMageRevertYear} 年将恢复为人类。";
            Debug.Log(log);
            WriteLeaderboardToFile(log + Environment.NewLine);

            // 只给使徒本人标记“深渊低语”
            try
            {
                if (mage != null && mage.hasHealth() && !mage.hasTrait("whispers_from_the_abyss"))
                {
                    mage.addTrait("whispers_from_the_abyss");
                }
            }
            catch { }



            NotificationHelper.ShowThisMessage(
                $"<color=#FF3333>【恶魔使徒降临】</color>\n" +
                $"<color=#FFFFFF>世界历 {currentYear} 年，{mage.name}</color>\n" +
                $"<color=#FF9999>现世于 {where}，自带 5000 击杀！</color>\n" +
                $"<color=#AAAAAA>若存活 {EVIL_RED_MAGE_SURVIVE_YEARS} 年将重获人身。</color>"
            );
        }

        /// <summary>
        /// 若恶魔使徒连续存活 88 年，则自动还原为人类；若提前死亡，清理状态并记录日志。
        /// </summary>
        private static void CheckEvilRedMageRevert(int yearNow)
        {
            // 没有在场的法师：若上一个已死，确保状态清理完毕
            if (EvilRedMage == null)
                return;

            // 先检查是否死亡/离场
            if (EvilRedMage.isRekt() || !EvilRedMage.hasHealth())
            {
                string name = EvilRedMage?.name ?? "未知法师";
                Debug.Log($"[恶魔使徒] 已陨落：{name}");
                WriteLeaderboardToFile($"[恶魔使徒] 世界历 {yearNow} 年：{name} 已被击杀，未能重获人身。\n");

                // 清空状态
                EvilRedMage = null;
                EvilRedMageSpawnYear = -1;
                EvilRedMageRevertYear = -1;
                EvilRedMageOriginalName = null;
                return;
            }

            // 存活且达到/超过目标年份：自动恢复为人类
            if (EvilRedMageRevertYear > 0 && yearNow >= EvilRedMageRevertYear)
            {
                string demonName = EvilRedMage.name ?? "未知法师";

                // 先记住当前实体，再调用还原
                Actor toRevert = EvilRedMage;

                // 进行还原（调用你现有的人类化方法）
                bool ok = TurnIntoHuman(toRevert, null);

                if (ok)
                {
                    string msg =
                        $"[恶魔使徒·洗礼] 世界历 {yearNow} 年：{demonName} " +
                        $"存活满 {EVIL_RED_MAGE_SURVIVE_YEARS} 年，已恢复为人类。";
                    Debug.Log(msg);
                    WriteLeaderboardToFile(msg + Environment.NewLine);

                    NotificationHelper.ShowThisMessage(
                        $"<color=#FF6666>【恶魔使徒·洗礼】</color>\n" +
                        $"<color=#FFFFFF>{demonName}</color>\n" +
                        $"<color=#AAAAAA>存活满 {EVIL_RED_MAGE_SURVIVE_YEARS} 年，已重返人类之身。</color>"
                    );
                }
                else
                {
                    Debug.LogWarning("[恶魔使徒] 还原失败：TurnIntoHuman 返回 false。");
                }

                // 清空状态
                EvilRedMage = null;
                EvilRedMageSpawnYear = -1;
                EvilRedMageRevertYear = -1;
                EvilRedMageOriginalName = null;
            }
        }

        /// <summary>
        /// 寻找一个尽量“有戏”的落点：优先城市区域；否则丘陵；最后随机已有地块。
        /// </summary>
        /// <summary>
        /// 从丘陵地形中随机选一个地块作为落点（与 SpawnRandomUnit 的写法一致）
        /// </summary>
        private static WorldTile FindAnyGoodSpawnTile()
        {
            try
            {
                var hillTiles = TileLibrary.hills.getCurrentTiles();
                if (hillTiles != null && hillTiles.Count > 0)
                {
                    int randomTileIndex = UnityEngine.Random.Range(0, hillTiles.Count);
                    return hillTiles[randomTileIndex];
                }
                return null; // 没有丘陵地块就跳过
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[恶魔使徒] 寻找落点异常：{ex.Message}");
                return null;
            }
        }


        #endregion



        #region  存档相关
        // ==== 存档相关（放在 traitAction 内） ====
        public static void WriteWorldEventSilently(string line)
        {

            if (!_txtLogEnabled) return;
            try
            {
                if (string.IsNullOrEmpty(currentSessionFile))
                {
                    // 优先走“按槽位的新局文件”
                    var slot = string.IsNullOrWhiteSpace(_saveSlotName) ? "slot" : _saveSlotName;
                    BeginNewGameSession(slot);
                }
                System.IO.File.AppendAllText(currentSessionFile, line, Encoding.UTF8);
            }
            catch { /* 静默 */ }
        }

        // 保留这一个定义（删掉重复的那份）
        static string _saveSlotName;
        private static string _currentSessionDir;

        public static void SetSaveSlot(string slot)
        {
            if (!_txtLogEnabled) return;
            // 只记录槽位名，避免覆盖“新局时间戳文件”
            _saveSlotName = string.IsNullOrWhiteSpace(slot) ? "slot"
                            : string.Concat(slot.Select(ch => System.IO.Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

            // 如果本局还没建文件，就直接当场开一份“新局文件”
            if (string.IsNullOrEmpty(currentSessionFile) || !File.Exists(currentSessionFile))
            {
                BeginNewGameSession(_saveSlotName);
            }
            // 如果已经有文件（这局刚建好的），则不再改 `currentSessionFile`
        }

        // 每次开新局都新建一个全新的 txt（按槽位分文件夹）
        public static void BeginNewGameSession(string slotDisplayName)
        {
            if (!_txtLogEnabled) return;

            try
            {

                _saveSlotName = string.IsNullOrWhiteSpace(slotDisplayName) ? "slot"
                    : string.Concat(slotDisplayName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

                if (!Directory.Exists(LogDirectory)) Directory.CreateDirectory(LogDirectory);
                _currentSessionDir = Path.Combine(LogDirectory, $"slot_{_saveSlotName}");
                if (!Directory.Exists(_currentSessionDir)) Directory.CreateDirectory(_currentSessionDir);
                // 你原有的建档/复位逻辑...
                DemonGameRules2.code.traitAction.ApplyYearZeroAndPurge_ArmedFileOnce();



                string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                currentSessionFile = Path.Combine(_currentSessionDir, $"leaderboard_{ts}.txt");

                var header = new StringBuilder();
                header.AppendLine($"存档槽位: {_saveSlotName}");
                header.AppendLine($"新局创建时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                header.AppendLine(new string('=', 50));
                header.AppendLine();
                File.WriteAllText(currentSessionFile, header.ToString(), Encoding.UTF8);

                Debug.Log($"[编年史] 新局文件: {currentSessionFile}");
                
            }
            catch (Exception ex)
            {
                Debug.LogError($"[编年史] 创建新局文件失败: {ex.Message}");
                currentSessionFile = null;
            }
        }




        #endregion

        







    }
}

