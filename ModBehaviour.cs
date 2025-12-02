using Duckov;
using Duckov.Buffs;
using Duckov.Endowment;
using Duckov.Endowment.UI;
using Duckov.Modding;
using Duckov.UI;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using SodaCraft.Localizations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DuckovSkills
{
	public class DisplaySkillValueConfig
	{
		// All
		public string hotKey = "<Keyboard>/alt";
		public int skillCoolDown = 90;// 技能冷却时间
		public int skillDuration = 30;// 技能持续时间

		// Default
		public string defaultName = ModBehaviour.IS_CHINESE ? "默认角色技能" : "Default Role Skills";
		public bool defaultRemoveBleeding = true;
		public float defaultAddHealth = 15f;

		// Surviver
		public string surviverName = ModBehaviour.IS_CHINESE ? "幸存者技能" : "Surviver Skills";
		public bool surviverImmuneBleeding = true;
		public bool surviverImmunePoison = true;
		public bool surviverImmuneElectric = true;
		public bool surviverImmuneBurning = true;
		public bool surviverImmuneNauseous = true;
		public bool surviverImmuneStun = true;
		public float surviverAddHealthTickPercent = 10f;

		// Porter
		public string porterName = ModBehaviour.IS_CHINESE ? "搬运者技能" : "Porter Skills";
		public float porterAddWalkSpeed = 3.5f;
		public float porterAddRunSpeed = 3.5f;
		public float porterAddMaxWeight = 20f;

		// Berserker
		public string berserkerName = ModBehaviour.IS_CHINESE ? "狂战士技能" : "Berserker Skills";
		public float berserkerAddLifeStealPercent = 30f;
		public float berserkerAddStaminaDrainRate = -1.2f;
		public float berserkerAddBodyArmor = 3f;
		public float berserkerAddHeadArmor = 3f;
		public float berserkerAddGunDamageMultiplier = -0.75f;
		public float berserkerAddMeleeDamageMultiplier = 0.25f;
		public float berserkerAddMeleeCritRateGain = 1f;
		public float berserkerAddMeleeCritDamageGain = 0.5f;
		public float berserkerAddWalkSoundRange = -0.8f;
		public float berserkerAddRunSoundRange = -0.8f;

		// Marksman
		public string marksmanName = ModBehaviour.IS_CHINESE ? "枪手" : "Marksman";
		public float marksmanAddGunDamageMultiplier = 0.15f;
		public float marksmanAddGunCritRateGain = 0.3f;
		public float marksmanAddGunCritDamageGain = 0.5f;
		public float marksmanAddGunDistanceMultiplier = 0.25f;
		public float marksmanAddGunScatterMultiplier = -0.4f;
		public float marksmanAddRecoilControl = 0.3f;
	}
	public class ModBehaviour : Duckov.Modding.ModBehaviour
	{
		// 根据当前语言设置描述文字
		private static SystemLanguage[] CHINESE_LANGUAGES = {
				SystemLanguage.Chinese,
				SystemLanguage.ChineseSimplified,
				SystemLanguage.ChineseTraditional
			};

		public static bool IS_CHINESE = CHINESE_LANGUAGES.Contains(LocalizationManager.CurrentLanguage);
		private static string MOD_NAME = IS_CHINESE ? "角色专属技能" : "Character Skills";
		DisplaySkillValueConfig config = new DisplaySkillValueConfig();
		private static string persistentConfigPath => Path.Combine(Application.streamingAssetsPath, "DuckovSkillsConfig.txt");
		//private const float SKILL_COOLDOWN = 90f;// 技能冷却时间
		private const float TICK_TIME = 1f;// 独立每秒触发计时器间隔时间

		private bool isInCoolDown = false;// 技能是否进入冷却状态
		private float skillStartTime = -1f;// 技能开始时间
		private float skillDuration = 0f;// 技能的持续时间
		private float surviverNextTickTime = -1f;// 独立每秒触发计时器
		private EndowmentIndex currentEndowmentIndex = EndowmentIndex.None;// 当前激活技能的角色天赋索引
		private InputAction activateSkillAction = null;// 快捷键

		// buff UI
		private CharacterBuffManager buffManager = null;
		private Buff activeSkillTemplate = null;    // 模板: 用于“技能激活”
		private Buff cooldownSkillTemplate = null;  // 模板: 用于“技能冷却”

		// 本地化
		private static bool translationsRegistered = false;
		private TextMeshProUGUI skillShowText = null;// 技能标题

		// 使用延迟加载属性 (Lazy-loading properties) 来确保UI元素只被创建一次
		private TextMeshProUGUI SkillShowText
		{
			get
			{
				if (skillShowText == null)
				{
					skillShowText = Instantiate(GameplayDataSettings.UIStyle.TemplateTextUGUI);
					skillShowText.name = "SkillShowText";
					skillShowText.alignment = TextAlignmentOptions.Left;
					skillShowText.fontSize = 40f; // 可以根据需要调整
					skillShowText.enableWordWrapping = true;// 允许文本换行
				}
				return skillShowText;
			}
		}

		private void SkillInit()
		{
			isInCoolDown = false;
			skillDuration = 0f;
			skillStartTime = -1f;
			currentEndowmentIndex = EndowmentIndex.None;
			surviverNextTickTime = -1f;
			Health.OnHurt -= OnHurt;// 防止订阅之后因某些原因导致没有退订
			buffManager = CharacterMainControl.Main.GetBuffManager();
		}
		void Awake()
		{
			Debug.Log("DuckovSkills Loaded!!!");
		}
		void OnDestroy()
		{
			if (skillShowText != null)
				Destroy(skillShowText);
		}
		void OnEnable()
		{
			if (!translationsRegistered)
			{
				RegisterLocalizations();
				translationsRegistered = true;
			}
			activateSkillAction = new InputAction("ActivateSkill", InputActionType.Button, "<Keyboard>/alt");
			activateSkillAction.performed += OnActivateSkill;
			activateSkillAction.Enable();
			LevelManager.OnLevelInitialized += SkillInit;
			skillStartTime = -1f;

			activeSkillTemplate = CreateBuffTemplate("duckovskills.buff.active.name", "duckovskills.buff.active.desc", Color.red, 7000);
			//Debug.Log($"创建技能激活模板完成activeSkillTemplate: {activeSkillTemplate} ");
			cooldownSkillTemplate = CreateBuffTemplate("duckovskills.buff.cooldown.name", "duckovskills.buff.cooldown.desc", Color.gray, 7001);
			//Debug.Log($"创建技能冷却模板完成cooldownSkillTemplate: {cooldownSkillTemplate} ");

			ModManager.OnModActivated += OnModActivated;

			// 立即检查一次，防止 ModConfig 已经加载但事件错过了
			if (ModConfigAPI.IsAvailable())
			{
				Debug.Log("DuckovSkills: ModConfig already available!");
				LoadConfigFromModConfig();
				SetupModConfig();
			}
			ManagedUIElement.onOpen += this.OnManagedUIBehaviorOpen;
			ManagedUIElement.onClose += this.OnManagedUIBehaviorClose;
		}
		void OnDisable()
		{
			RemoveLocalizations();
			translationsRegistered = false;
			if (activateSkillAction != null)
			{
				activateSkillAction.performed -= OnActivateSkill;
				activateSkillAction.Disable();
				activateSkillAction.Dispose();
				activateSkillAction = null;
			}
			LevelManager.OnLevelInitialized -= SkillInit;
			Health.OnHurt -= OnHurt;
			skillStartTime = -1f;

			if (activeSkillTemplate != null) Destroy(activeSkillTemplate.gameObject);
			if (cooldownSkillTemplate != null) Destroy(cooldownSkillTemplate.gameObject);
			activeSkillTemplate = null;
			cooldownSkillTemplate = null;
			buffManager = null;

			ModManager.OnModActivated -= OnModActivated;
			ModConfigAPI.SafeRemoveOnOptionsChangedDelegate(OnModConfigOptionsChanged);

			ManagedUIElement.onOpen -= this.OnManagedUIBehaviorOpen;
			ManagedUIElement.onClose -= this.OnManagedUIBehaviorClose;
		}
		private void Update()
		{
			if (skillStartTime != -1f)
			{
				if (Time.time >= skillStartTime + skillDuration + config.skillCoolDown)
				{// 冷却结束
					skillStartTime = -1f;
					isInCoolDown = false;
					return;
				}
				if (Time.time >= skillStartTime + skillDuration && !isInCoolDown)
				{// 技能结束
					isInCoolDown = true;
					OnRemoveSkill();
					// 添加冷却Buff
					if (buffManager != null && cooldownSkillTemplate != null)
					{
						typeof(Buff)
							.GetField("totalLifeTime", BindingFlags.NonPublic | BindingFlags.Instance)
							?.SetValue(cooldownSkillTemplate, config.skillCoolDown);

						buffManager.AddBuff(cooldownSkillTemplate, CharacterMainControl.Main);
					}
				}
				if (currentEndowmentIndex == EndowmentIndex.Surviver && surviverNextTickTime <= skillStartTime + skillDuration && Time.time >= surviverNextTickTime)
				{
					surviverNextTickTime = Time.time + TICK_TIME; // 设置下一次Tick的时间
					SurviverSkill(CharacterMainControl.Main, 0f, true);
					//Debug.Log($"SurviverSkill Tick Time Remaining: {skillDuration} seconds");
				}

			}
			//if (Input.GetKeyDown(KeyCode.Q))
			//{
			//	//CharacterMainControl.Main.AddBuff(GameplayDataSettings.Buffs.BleedSBuff);
				
			//}
		}
		private void OnManagedUIBehaviorOpen(ManagedUIElement obj)
		{
			Debug.Log($"OnManagedUIBehaviorOpen: {obj.name}");
			if (obj is PlayerStatsView playerStatsView)
			{
				//Debug.Log("DuckovSkills: PlayerStatsView opened. Injecting UI...");

				// 激活文本对象，以便可以访问其组件
				SkillShowText.gameObject.SetActive(true);

				RectTransform textRectTransform = SkillShowText.transform as RectTransform;
				if (textRectTransform == null) return;

				Transform parentContainer = playerStatsView.transform.parent;
				if (parentContainer != null)
				{
					Debug.Log("DuckovSkills: playerStatsView.transform.parent");
					textRectTransform.SetParent(parentContainer, false);
				}
				else
				{
					textRectTransform.SetParent(PlayerStatsView.Instance.transform);
				}

				// 设置锚点 (Anchors) 为中心
				textRectTransform.anchorMin = new Vector2(0.6f, 0.65f);
				textRectTransform.anchorMax = new Vector2(0.6f, 0.4f);

				// 设置轴心 (Pivot) 为中心
				textRectTransform.pivot = new Vector2(0.5f, 0.5f);

				//// Y轴偏移300，让它显示在屏幕上半部分
				textRectTransform.anchoredPosition = new Vector2(0, 100);
				//// (可选) 设置尺寸
				textRectTransform.sizeDelta = new Vector2(600, 300);

				// 设置内容
				textRectTransform.localScale = Vector3.one;
				switch (EndowmentManager.CurrentIndex)
				{
					case EndowmentIndex.None:
						SkillShowText.text = $"<color=red>{"duckovskills.endowment.title".ToPlainText()}</color>\n<color=white>{"duckovskills.endowment.none.desc".ToPlainText()}</color>";
						break;
					case EndowmentIndex.Surviver:
						SkillShowText.text = $"<color=red>{"duckovskills.endowment.title".ToPlainText()}</color>\n<color=white>{"duckovskills.endowment.surviver.desc".ToPlainText()}</color>";
						break;
					case EndowmentIndex.Porter:
						SkillShowText.text = $"<color=red>{"duckovskills.endowment.title".ToPlainText()}</color>\n<color=white>{"duckovskills.endowment.porter.desc".ToPlainText()}</color>";
						break;
					case EndowmentIndex.Berserker:
						SkillShowText.text = $"<color=red>{"duckovskills.endowment.title".ToPlainText()}</color>\n<color=white>{"duckovskills.endowment.berserker.desc".ToPlainText()}</color>";
						break;
					case EndowmentIndex.Marksman:
						SkillShowText.text = $"<color=red>{"duckovskills.endowment.title".ToPlainText()}</color>\n<color=white>{"duckovskills.endowment.marksman.desc".ToPlainText()}</color>";
						break;
					default:
						break;
				}
				
			}
			else
			{
				SkillShowText.gameObject.SetActive(false);
			}
		}
		private void OnManagedUIBehaviorClose(ManagedUIElement obj)
		{
			Debug.Log($"OnManagedUIBehaviorClose: {obj.name}");
			if (obj is PlayerStatsView playerStatsView)
			{
				SkillShowText.gameObject.SetActive(false);
			}	
		}
		private void SetSkillDuration(float duration = 30f)
		{
			skillDuration = duration;
		}
		private bool IsSkillActive()
		{
			if (skillStartTime == -1f)
			{
				return false;
			}
			return Time.time < skillStartTime + skillDuration;
		}
		private bool IsSkillOnCooldown()
		{
			if (skillStartTime == -1f)
			{
				return false;
			}
			return Time.time >= skillStartTime + skillDuration && Time.time < skillStartTime + skillDuration + config.skillCoolDown;
		}

		private void OnActivateSkill(InputAction.CallbackContext context)
		{// 触发技能
			if (skillStartTime != -1f)
			{
				if (IsSkillActive())
				{
					CharacterMainControl.Main.PopText("duckovskills.poptext.active".ToPlainText());
				}
				else
				{
					CharacterMainControl.Main.PopText("duckovskills.poptext.cooldown".ToPlainText());
				}
				return;
			}
			CharacterMainControl character = CharacterMainControl.Main;
			if (character == null)
			{
				return;
			}
			//Debug.Log("--------------------------------------------------------------------");
			//Debug.Log("--------------------------------------------------------------------");
			isInCoolDown = false;
			currentEndowmentIndex = EndowmentManager.CurrentIndex;
			switch (currentEndowmentIndex)
			{
				case EndowmentIndex.None:
					{
						NoneSkill(character);
						break;
					}
				case EndowmentIndex.Surviver:
					{
						SurviverSkill(character, config.skillDuration);
						break;
					}
				case EndowmentIndex.Porter:
					{
						PorterSkill(character, false, config.skillDuration);
						break;
					}
				case EndowmentIndex.Berserker:
					{
						BerserkerSkill(character, false, config.skillDuration);
						break;
					}
				case EndowmentIndex.Marksman:
					{
						MarksmanSkill(character, false, config.skillDuration);
						break;
					}
				default:
					{
						character.PopText("duckovskills.poptext.nothing".ToPlainText());
						break;
					}
			}
			// 将配置好的模板交给Buff管理器
			if (skillStartTime != -1f && buffManager != null && activeSkillTemplate != null)
			{
				//Debug.Log($"OnActivateSkill中准备激活: {activeSkillTemplate} ");
				typeof(Buff).GetField("totalLifeTime", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(activeSkillTemplate, skillDuration);// 在应用前，设置好这次的持续时间
				buffManager.AddBuff(activeSkillTemplate, character);
				//Debug.Log($"activeSkillTemplate DisplayName: {activeSkillTemplate.DisplayName} ");
				//Debug.Log($"activeSkillTemplate Icon: {activeSkillTemplate.Icon} ");
				//Debug.Log($"activeSkillTemplate ID: {activeSkillTemplate.ID} ");
				//Debug.Log($"activeSkillTemplate CurrentLifeTime: {activeSkillTemplate.CurrentLifeTime} ");
				//Debug.Log($"activeSkillTemplate Description: {activeSkillTemplate.Description} ");
			}
			else
			{
				//Debug.Log($"OnActivateSkill中无法激活: {activeSkillTemplate} buffManager: {buffManager}");
			}
		}
		private void OnRemoveSkill()
		{// 移除技能效果
			if (CharacterMainControl.Main == null)
			{
				return;
			}
			switch (currentEndowmentIndex)
			{
				case EndowmentIndex.None:
					{// 无需移除效果
						break;
					}
				case EndowmentIndex.Surviver:
					{// 无需移除效果
						if (CharacterMainControl.Main)
						{
							CharacterMainControl.Main.PopText("duckovskills.poptext.powerlost".ToPlainText());
						}
						break;
					}
				case EndowmentIndex.Porter:
					{
						PorterSkill(CharacterMainControl.Main, true);
						break;
					}
				case EndowmentIndex.Berserker:
					{
						BerserkerSkill(CharacterMainControl.Main, true);
						break;
					}
				case EndowmentIndex.Marksman:
					{
						MarksmanSkill(CharacterMainControl.Main, true);
						break;
					}
				default:
					{
						break;
					}
			}
		}
		private void NoneSkill(CharacterMainControl character, float continuetime = 0f)
		{// 默认角色
		 // 移除流血状态，恢复15hp
			if (character == null)
			{
				return;
			}
			if (config.defaultRemoveBleeding)
			{
				character.RemoveBuffsByTag(Buff.BuffExclusiveTags.Bleeding, false);// 流血
			}
			character.Health.AddHealth(config.defaultAddHealth);// 恢复15点生命值
			
			SetSkillDuration(continuetime);
			skillStartTime = Time.time;
			character.PopText("duckovskills.poptext.bandage".ToPlainText());
			character.Quack();

		}
		private void SurviverSkill(CharacterMainControl character, float continuetime = 30f, bool tickTime = false)
		{// 幸存者（最大生命值+5 身体护甲 +1 头部护甲 +1 行走速度-0.5 奔跑速度-0.5）
		 // 移除并暂时免疫流血、中毒、电击、燃烧、恶心、眩晕负面状态  每秒恢复最大生命值10%（最少为5HP）
			if (character == null)
			{
				return;
			}
			// tick技能每秒触发一次（触发时会立刻执行一次）
			if (config.surviverImmuneBleeding)
			{
				character.RemoveBuffsByTag(Buff.BuffExclusiveTags.Bleeding, false);// 流血
			}
			if (config.surviverImmunePoison)
			{
				character.RemoveBuffsByTag(Buff.BuffExclusiveTags.Poison, false);// 中毒
			}
			if (config.surviverImmuneElectric)
			{
				character.RemoveBuffsByTag(Buff.BuffExclusiveTags.Electric, false);// 电击
			}
			if (config.surviverImmuneBurning)
			{
				character.RemoveBuffsByTag(Buff.BuffExclusiveTags.Burning, false);// 燃烧
			}
			if (config.surviverImmuneNauseous)
			{
				character.RemoveBuffsByTag(Buff.BuffExclusiveTags.Nauseous, false);// 恶心
			}
			if (config.surviverImmuneStun)
			{
				character.RemoveBuffsByTag(Buff.BuffExclusiveTags.Stun, false);// 眩晕
			}
			float heal = Mathf.Max(character.Health.MaxHealth * config.surviverAddHealthTickPercent * 0.01f, 5f);// 每秒恢复最大生命10% 最少为5点
			character.Health.AddHealth(heal);// 每秒恢复最大生命10%
			
			if (!tickTime)
			{// 技能触发
				SetSkillDuration(continuetime);
				skillStartTime = Time.time;
				surviverNextTickTime = Time.time + TICK_TIME; // 初始化下一次Tick的时间

				character.PopText("duckovskills.poptext.nopain".ToPlainText());
				character.Quack();
			}
		}
		private void PorterSkill(CharacterMainControl character, bool remove = false, float continuetime = 30f)
		{// 搬运者（最大生命值-15% 最大负重+10 背包空间+5 奔跑声音距离-20%）
		 // 恢复10HP 行走速度+3.5 奔跑速度+3.5 最大负重+20
			if (character == null)
			{
				return;
			}
			if (!remove)
			{
				character.Health.AddHealth(10f);// 恢复10点生命值
				Item characterItem = character.CharacterItem;
				if (characterItem)
				{
					//Debug.Log($"WalkSpeed: {character.CharacterWalkSpeed}");
					//Debug.Log($"CharacterRunSpeed: {character.CharacterRunSpeed}");
					//Debug.Log($"MaxWeight: {character.MaxWeight}");
					characterItem.GetStat("WalkSpeed".GetHashCode())?.AddModifier(new Modifier(ModifierType.Add, config.porterAddWalkSpeed, this));
					characterItem.GetStat("RunSpeed".GetHashCode())?.AddModifier(new Modifier(ModifierType.Add, config.porterAddRunSpeed, this));
					characterItem.GetStat("MaxWeight".GetHashCode())?.AddModifier(new Modifier(ModifierType.Add, config.porterAddMaxWeight, this));

					//Debug.Log($"Modify   WalkSpeed: {character.CharacterWalkSpeed}");
					//Debug.Log($"Modify   CharacterRunSpeed: {character.CharacterRunSpeed}");
					//Debug.Log($"Modify   MaxWeight: {character.MaxWeight}");
				}
				SetSkillDuration(continuetime);
				skillStartTime = Time.time;
				character.PopText("duckovskills.poptext.adrenaline".ToPlainText());
				character.Quack();

			}
			else
			{
				Item characterItem = character.CharacterItem;
				if (characterItem)
				{
					characterItem.GetStat("WalkSpeed".GetHashCode())?.RemoveAllModifiersFromSource(this);
					characterItem.GetStat("RunSpeed".GetHashCode())?.RemoveAllModifiersFromSource(this);
					characterItem.GetStat("MaxWeight".GetHashCode())?.RemoveAllModifiersFromSource(this);

					//Debug.Log($"Remove   WalkSpeed: {character.CharacterWalkSpeed}");
					//Debug.Log($"Remove   CharacterRunSpeed: {character.CharacterRunSpeed}");
					//Debug.Log($"Remove   MaxWeight: {character.MaxWeight}");
				}
				character.PopText("duckovskills.poptext.powerlost".ToPlainText());
			}
		}
		private void BerserkerSkill(CharacterMainControl character, bool remove = false, float continuetime = 30f)
		{// 狂战士（最大生命值+7 移动能力+10% 近战伤害倍率+25% 枪械伤害倍率-25% 奔跑声音距离-10% 行走声音距离-10%）
		 // 增加30%的吸血 耐力消耗率-1.2 身体护甲+3 头部护甲+3 枪械伤害倍率-75% 近战伤害倍率+25% 近战暴击增益+100% 近战暴伤增益+50% 行走声音距离-80% 奔跑声音距离-80% 
			if (character == null)
			{
				return;
			}
			if (!remove)
			{
				Item characterItem = character.CharacterItem;
				if (characterItem)
				{
					//Debug.Log($"StaminaDrainRate: {character.StaminaDrainRate}");
					//Debug.Log($"BodyArmor: {character.Health.BodyArmor}");
					//Debug.Log($"HeadArmor: {character.Health.HeadArmor}");
					//Debug.Log($"GunDamageMultiplier: {character.GunDamageMultiplier}");
					//Debug.Log($"MeleeDamageMultiplier: {character.MeleeDamageMultiplier}");
					//Debug.Log($"MeleeCritRateGain: {character.MeleeCritRateGain}");
					//Debug.Log($"MeleeCritDamageGain: {character.MeleeCritDamageGain}");
					//Debug.Log($"WalkSoundRange: {character.WalkSoundRange}");
					//Debug.Log($"RunSoundRange: {character.RunSoundRange}");

					Health.OnHurt += OnHurt;
					characterItem.GetStat("StaminaDrainRate".GetHashCode())?.AddModifier(new Modifier(ModifierType.Add, config.berserkerAddStaminaDrainRate, this));
					characterItem.GetStat("BodyArmor".GetHashCode())?.AddModifier(new Modifier(ModifierType.Add, config.berserkerAddBodyArmor, this));
					characterItem.GetStat("HeadArmor".GetHashCode())?.AddModifier(new Modifier(ModifierType.Add, config.berserkerAddHeadArmor, this));
					characterItem.GetStat("GunDamageMultiplier".GetHashCode())?.AddModifier(new Modifier(ModifierType.Add, config.berserkerAddGunDamageMultiplier, this));
					characterItem.GetStat("MeleeDamageMultiplier".GetHashCode())?.AddModifier(new Modifier(ModifierType.Add, config.berserkerAddMeleeDamageMultiplier, this));
					characterItem.GetStat("MeleeCritRateGain".GetHashCode())?.AddModifier(new Modifier(ModifierType.Add, config.berserkerAddMeleeCritRateGain, this));
					characterItem.GetStat("MeleeCritDamageGain".GetHashCode())?.AddModifier(new Modifier(ModifierType.Add, config.berserkerAddMeleeCritDamageGain, this));
					characterItem.GetStat("WalkSoundRange".GetHashCode())?.AddModifier(new Modifier(ModifierType.PercentageAdd, config.berserkerAddWalkSoundRange, this));
					characterItem.GetStat("RunSoundRange".GetHashCode())?.AddModifier(new Modifier(ModifierType.PercentageAdd, config.berserkerAddRunSoundRange, this));

					//Debug.Log($"Modify   StaminaDrainRate: {character.StaminaDrainRate}");
					//Debug.Log($"Modify   BodyArmor: {character.Health.BodyArmor}");
					//Debug.Log($"Modify   HeadArmor: {character.Health.HeadArmor}");
					//Debug.Log($"Modify   GunDamageMultiplier: {character.GunDamageMultiplier}");
					//Debug.Log($"Modify   MeleeDamageMultiplier: {character.MeleeDamageMultiplier}");
					//Debug.Log($"Modify   MeleeCritRateGain: {character.MeleeCritRateGain}");
					//Debug.Log($"Modify   MeleeCritDamageGain: {character.MeleeCritDamageGain}");
					//Debug.Log($"Modify   WalkSoundRange: {character.WalkSoundRange}");
					//Debug.Log($"Modify   RunSoundRange: {character.RunSoundRange}");
				}

				SetSkillDuration(continuetime);
				skillStartTime = Time.time;
				character.PopText("duckovskills.poptext.berserker".ToPlainText());
				character.Quack();
			}
			else
			{
				Item characterItem = CharacterMainControl.Main.CharacterItem;
				if (characterItem)
				{
					Health.OnHurt -= OnHurt;

					characterItem.GetStat("StaminaDrainRate".GetHashCode())?.RemoveAllModifiersFromSource(this);
					characterItem.GetStat("BodyArmor".GetHashCode())?.RemoveAllModifiersFromSource(this);
					characterItem.GetStat("HeadArmor".GetHashCode())?.RemoveAllModifiersFromSource(this);
					characterItem.GetStat("GunDamageMultiplier".GetHashCode())?.RemoveAllModifiersFromSource(this);
					characterItem.GetStat("MeleeDamageMultiplier".GetHashCode())?.RemoveAllModifiersFromSource(this);
					characterItem.GetStat("MeleeCritRateGain".GetHashCode())?.RemoveAllModifiersFromSource(this);
					characterItem.GetStat("MeleeCritDamageGain".GetHashCode())?.RemoveAllModifiersFromSource(this);
					characterItem.GetStat("WalkSoundRange".GetHashCode())?.RemoveAllModifiersFromSource(this);
					characterItem.GetStat("RunSoundRange".GetHashCode())?.RemoveAllModifiersFromSource(this);

					//Debug.Log($"Remove   StaminaDrainRate: {character.StaminaDrainRate}");
					//Debug.Log($"Remove   BodyArmor: {character.Health.BodyArmor}");
					//Debug.Log($"Remove   HeadArmor: {character.Health.HeadArmor}");
					//Debug.Log($"Remove   GunDamageMultiplier: {character.GunDamageMultiplier}");
					//Debug.Log($"Remove   MeleeDamageMultiplier: {character.MeleeDamageMultiplier}");
					//Debug.Log($"Remove   MeleeCritRateGain: {character.MeleeCritRateGain}");
					//Debug.Log($"Remove   MeleeCritDamageGain: {character.MeleeCritDamageGain}");
					//Debug.Log($"Remove   WalkSoundRange: {character.WalkSoundRange}");
					//Debug.Log($"Remove   RunSoundRange: {character.RunSoundRange}");
				}
				character.PopText("duckovskills.poptext.powerlost".ToPlainText());
			}

		}
		private void MarksmanSkill(CharacterMainControl character, bool remove = false, float continuetime = 20f)
		{// 枪手（最大生命值-17.5% 枪械伤害倍率+15% 枪械射程系数+15% 后座力控制+0.1）
		 // 枪械伤害倍率+15% 枪械暴率系数+30% 枪械爆伤系数+50% 枪械射程系数+25% 散射控制+40% 后座力控制+0.3
			if (character == null)
			{
				return;
			}
			if (!remove)
			{
				Item characterItem = CharacterMainControl.Main.CharacterItem;
				if (characterItem)
				{
					//Debug.Log($"GunDamageMultiplier: {CharacterMainControl.Main.GunDamageMultiplier}");
					//Debug.Log($"MeleeDamageMultiplier: {CharacterMainControl.Main.GunCritRateGain}");
					//Debug.Log($"MeleeCritRateGain: {CharacterMainControl.Main.GunCritDamageGain}");
					//Debug.Log($"MeleeCritDamageGain: {CharacterMainControl.Main.GunDistanceMultiplier}");
					//Debug.Log($"WalkSoundRange: {CharacterMainControl.Main.GunScatterMultiplier}");

					characterItem.GetStat("GunDamageMultiplier".GetHashCode())?.AddModifier(new Modifier(ModifierType.Add, config.marksmanAddGunDamageMultiplier, this));
					characterItem.GetStat("GunCritRateGain".GetHashCode())?.AddModifier(new Modifier(ModifierType.Add, config.marksmanAddGunCritRateGain, this));
					characterItem.GetStat("GunCritDamageGain".GetHashCode())?.AddModifier(new Modifier(ModifierType.Add, config.marksmanAddGunCritDamageGain, this));
					characterItem.GetStat("GunDistanceMultiplier".GetHashCode())?.AddModifier(new Modifier(ModifierType.Add, config.marksmanAddGunDistanceMultiplier, this));
					characterItem.GetStat("GunScatterMultiplier".GetHashCode())?.AddModifier(new Modifier(ModifierType.Add, config.marksmanAddGunScatterMultiplier, this));
					characterItem.GetStat("RecoilControl".GetHashCode())?.AddModifier(new Modifier(ModifierType.Add, config.marksmanAddRecoilControl, this));

					//Debug.Log($"Modify   GunDamageMultiplier: {CharacterMainControl.Main.GunDamageMultiplier}");
					//Debug.Log($"Modify   MeleeDamageMultiplier: {CharacterMainControl.Main.GunCritRateGain}");
					//Debug.Log($"Modify   MeleeCritRateGain: {CharacterMainControl.Main.GunCritDamageGain}");
					//Debug.Log($"Modify   MeleeCritDamageGain: {CharacterMainControl.Main.GunDistanceMultiplier}");
					//Debug.Log($"Modify   WalkSoundRange: {CharacterMainControl.Main.GunScatterMultiplier}");
				}

				SetSkillDuration(continuetime);
				skillStartTime = Time.time;
				character.PopText("duckovskills.poptext.marksman".ToPlainText());
				character.Quack();
			}
			else
			{
				Item characterItem = CharacterMainControl.Main.CharacterItem;
				if (characterItem)
				{

					characterItem.GetStat("GunDamageMultiplier".GetHashCode())?.RemoveAllModifiersFromSource(this);
					characterItem.GetStat("GunCritRateGain".GetHashCode())?.RemoveAllModifiersFromSource(this);
					characterItem.GetStat("GunCritDamageGain".GetHashCode())?.RemoveAllModifiersFromSource(this);
					characterItem.GetStat("GunDistanceMultiplier".GetHashCode())?.RemoveAllModifiersFromSource(this);
					characterItem.GetStat("GunScatterMultiplier".GetHashCode())?.RemoveAllModifiersFromSource(this);
					characterItem.GetStat("RecoilControl".GetHashCode())?.RemoveAllModifiersFromSource(this);

					//Debug.Log($"Remove   GunDamageMultiplier: {CharacterMainControl.Main.GunDamageMultiplier}");
					//Debug.Log($"Remove   MeleeDamageMultiplier: {CharacterMainControl.Main.GunCritRateGain}");
					//Debug.Log($"Remove   MeleeCritRateGain: {CharacterMainControl.Main.GunCritDamageGain}");
					//Debug.Log($"Remove   MeleeCritDamageGain: {CharacterMainControl.Main.GunDistanceMultiplier}");
					//Debug.Log($"Remove   WalkSoundRange: {CharacterMainControl.Main.GunScatterMultiplier}");
				}
				character.PopText("duckovskills.poptext.powerlost".ToPlainText());
			}
		}

		private void OnHurt(Health health, DamageInfo damageInfo)
		{
			CharacterMainControl character = CharacterMainControl.Main;
			if (character == null)
			{
				return;
			}
			if (damageInfo.fromCharacter.IsMainCharacter)
			{
				character.Health.AddHealth(damageInfo.damageValue * 0.3f);
				//Debug.Log($"伤害来自: {damageInfo.fromCharacter.name} 伤害数值: {damageInfo.damageValue}");
			}
		}


		// 这里是技能的UI相关代码
		private Buff CreateBuffTemplate(string nameKey, string descriptionKey, Color iconColor, int id)
		{
			// 创建一个临时的GameObject来承载Buff组件
			GameObject templateObject = new GameObject($"ModSkillBuff_{nameKey}");
			templateObject.SetActive(false); // 保持非激活状态，它只是一个模板
			DontDestroyOnLoad(templateObject); // 防止它在场景切换时被销毁

			// 将Buff组件添加到GameObject上
			Buff buffComponent = templateObject.AddComponent<Buff>();

			// 使用反射来设置私有的 [SerializeField] 字段 (因为不能直接访问它们)
			var type = typeof(Buff);
			type.GetField("id", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(buffComponent, id);
			type.GetField("displayName", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(buffComponent, nameKey);
			type.GetField("description", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(buffComponent, descriptionKey);
			type.GetField("icon", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(buffComponent, CreateDummySprite(iconColor));
			type.GetField("limitedLifeTime", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(buffComponent, true);
			
			return buffComponent;// totalLifeTime 会在应用Buff时动态设置
		}

		// 创建占位符Sprite的辅助方法 (保持不变)
		private Sprite CreateDummySprite(Color color)
		{
			int size = 60; // 贴图的尺寸 (例如 60x60)  ARGB32 格式支持透明通道
			Texture2D tex = new Texture2D(size, size, TextureFormat.ARGB32, false);

			float radius = size / 2f;
			Vector2 center = new Vector2(radius, radius);
			
			Color[] colors = new Color[size * size];// 性能考虑，先创建一个颜色数组，最后一次性应用所有像素

			// 优化边缘透明逻辑，实现简单额抗锯齿效果
			for (int y = 0; y < size; y++)
			{// 遍历所有像素位置 (x, y)
				for (int x = 0; x < size; x++)
				{
					float distanceToCenter = Vector2.Distance(new Vector2(x, y), center);// 计算当前像素到圆心的距离

					// 抗锯齿逻辑：
					// 如果距离比半径小一个像素以上，那么它肯定是完全不透明的
					if (distanceToCenter <= radius - 1)
					{
						colors[y * size + x] = color;
					}
					else if (distanceToCenter <= radius)
					{// 如果距离正好在半径的边缘（一个像素的宽度内）
					 // 我们根据它离真正边缘的距离，计算一个平滑的alpha值
					 // (radius - distanceToCenter) 会得到一个 0 到 1 之间的值
						float alpha = 1.0f - (distanceToCenter - (radius - 1));
						colors[y * size + x] = new Color(color.r, color.g, color.b, alpha);
					}
					else
					{// 如果距离大于半径，那么它就是完全透明的
						colors[y * size + x] = Color.clear;
					}
				}
			}

			tex.SetPixels(colors);// 颜色数组应用到贴图上			  
			tex.Apply();// 应用所有更改

			// 创建的贴图生成Sprite并返回
			return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
		}

		// 文本本地化处理
		private void RegisterLocalizations()
		{
			// 检查当前游戏设置的语言
			if (SodaCraft.Localizations.LocalizationManager.CurrentLanguage == SystemLanguage.ChineseSimplified || LocalizationManager.CurrentLanguage == SystemLanguage.Chinese
				|| LocalizationManager.CurrentLanguage == SystemLanguage.ChineseTraditional)
			{
				// --- 如果是中文，注册所有中文文本 ---
				// PopText 提示
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.poptext.active", "技能持续中！");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.poptext.cooldown", "技能冷却中！");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.poptext.powerlost", "力量消失了！");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.poptext.nopain", "无惧疼痛！");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.poptext.adrenaline", "肾上腺素！");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.poptext.berserker", "猛虎下山！");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.poptext.marksman", "超能光束！");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.poptext.bandage", "包扎！");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.poptext.nothing", "无事发生！");

				// Buff UI 文本
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.buff.active.name", "技能激活");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.buff.active.desc", "你的技能正在生效！");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.buff.cooldown.name", "技能冷却");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.buff.cooldown.desc", "等待技能再次可用。");

				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.endowment.title", "主动技能");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.endowment.none.desc", "包扎伤口，移除流血效果并恢复少量生命值");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.endowment.surviver.desc", "持续时间内免疫多种负面效果，并持续恢复生命值");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.endowment.porter.desc", "爆发肾上腺素，大幅提升移动速度和最大负重");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.endowment.berserker.desc", "进入狂暴状态，获得强大的近战增益和吸血效果");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.endowment.marksman.desc", "进入专注状态，大幅提升枪械的伤害、暴击和稳定性");
			}
			else
			{
				// --- 否则，默认使用英文 ---
				// PopText Tooltips
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.poptext.active", "Skill is active!");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.poptext.cooldown", "Skill on cooldown!");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.poptext.powerlost", "The power fades!");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.poptext.nopain", "Fear no pain!");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.poptext.adrenaline", "Adrenaline!");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.poptext.berserker", "Rush Down!");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.poptext.marksman", "Hyperbeam!");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.poptext.bandage", "Bandage Up!");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.poptext.nothing", "Nothing happened!");

				// Buff UI Text
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.buff.active.name", "Skill Active");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.buff.active.desc", "Your skill is in effect!");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.buff.cooldown.name", "Skill Cooldown");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.buff.cooldown.desc", "Waiting for the skill to be available again.");

				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.endowment.title", "Active Skill");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.endowment.none.desc", "Bandages wounds, removing bleeding and restoring a small amount of health.");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.endowment.surviver.desc", "Become immune to various negative effects and regenerate health over time.");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.endowment.porter.desc", "An adrenaline rush greatly increases movement speed and max weight capacity.");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.endowment.berserker.desc", "Enter a berserk rage, gaining powerful melee buffs and lifesteal.");
				SodaCraft.Localizations.LocalizationManager.SetOverrideText("duckovskills.endowment.marksman.desc", "Enter a state of focus, greatly enhancing firearm damage, criticals, and stability.");
			}

			Debug.Log("DuckovSkills: Localized texts registered.");
		}

		private void RemoveLocalizations()
		{
			// 移除所有注册的文本覆盖
			SodaCraft.Localizations.LocalizationManager.RemoveOverrideText("duckovskills.poptext.active");
			SodaCraft.Localizations.LocalizationManager.RemoveOverrideText("duckovskills.poptext.cooldown");
			SodaCraft.Localizations.LocalizationManager.RemoveOverrideText("duckovskills.poptext.powerlost");
			SodaCraft.Localizations.LocalizationManager.RemoveOverrideText("duckovskills.poptext.nopain");
			SodaCraft.Localizations.LocalizationManager.RemoveOverrideText("duckovskills.poptext.adrenaline");
			SodaCraft.Localizations.LocalizationManager.RemoveOverrideText("duckovskills.poptext.berserker");
			SodaCraft.Localizations.LocalizationManager.RemoveOverrideText("duckovskills.poptext.marksman");
			SodaCraft.Localizations.LocalizationManager.RemoveOverrideText("duckovskills.poptext.bandage");
			SodaCraft.Localizations.LocalizationManager.RemoveOverrideText("duckovskills.poptext.nothing");
			SodaCraft.Localizations.LocalizationManager.RemoveOverrideText("duckovskills.buff.active.name");
			SodaCraft.Localizations.LocalizationManager.RemoveOverrideText("duckovskills.buff.active.desc");
			SodaCraft.Localizations.LocalizationManager.RemoveOverrideText("duckovskills.buff.cooldown.name");
			SodaCraft.Localizations.LocalizationManager.RemoveOverrideText("duckovskills.buff.cooldown.desc");

			//SodaCraft.Localizations.LocalizationManager.RemoveOverrideText("duckovskills.endowment.title");
			//SodaCraft.Localizations.LocalizationManager.RemoveOverrideText("duckovskills.endowment.none.desc");
			//SodaCraft.Localizations.LocalizationManager.RemoveOverrideText("duckovskills.endowment.surviver.desc");
			//SodaCraft.Localizations.LocalizationManager.RemoveOverrideText("duckovskills.endowment.porter.desc");
			//SodaCraft.Localizations.LocalizationManager.RemoveOverrideText("duckovskills.endowment.berserker.desc");
			//SodaCraft.Localizations.LocalizationManager.RemoveOverrideText("duckovskills.endowment.marksman.desc");
			Debug.Log("DuckovSkills: Localized texts removed.");
		}


		// 这里自由配置技能数值相关函数
		private void OnModActivated(ModInfo info, Duckov.Modding.ModBehaviour behaviour)
		{
			if (info.name == ModConfigAPI.ModConfigName)
			{
				Debug.Log("DuckovSkills: ModConfig activated!");
				LoadConfigFromModConfig();
				SetupModConfig();
			}
		}
		private void SetupModConfig()
		{
			if (!ModConfigAPI.IsAvailable())
			{
				//Debug.LogWarning("DuckovSkills: ModConfig not available");
				return;
			}
			//Debug.Log("准备添加ModConfig配置项");
			// 添加配置变更监听
			ModConfigAPI.SafeAddOnOptionsChangedDelegate(OnModConfigOptionsChanged);
			// 添加配置项
			// 枪手选项
			ModConfigAPI.SafeAddInputWithSlider(
				MOD_NAME,
				"marksmanAddRecoilControl",
				IS_CHINESE ? "枪手增加后坐力控制" : "Berserker Add RecoilControl",
				typeof(float),
				config.marksmanAddRecoilControl,
				new Vector2(0.1f, 0.5f)
			);
			ModConfigAPI.SafeAddInputWithSlider(
				MOD_NAME,
				"marksmanAddGunScatterMultiplier",
				IS_CHINESE ? "枪手增加散射倍率" : "Berserker Add GunScatterMultiplier",
				typeof(float),
				config.marksmanAddGunScatterMultiplier,
				new Vector2(-0.6f, -0.1f)
			);
			ModConfigAPI.SafeAddInputWithSlider(
				MOD_NAME,
				"marksmanAddGunDistanceMultiplier",
				IS_CHINESE ? "枪手增加射程倍率" : "Berserker Add GunDistanceMultiplier",
				typeof(float),
				config.marksmanAddGunDistanceMultiplier,
				new Vector2(0.1f, 0.5f)
			);
			ModConfigAPI.SafeAddInputWithSlider(
				MOD_NAME,
				"marksmanAddGunCritDamageGain",
				IS_CHINESE ? "枪手增加枪械暴击伤害（非加算）" : "Berserker Add marksmanAddGunCritDamageGain",
				typeof(float),
				config.marksmanAddGunCritDamageGain,
				new Vector2(0.1f, 0.7f)
			);
			ModConfigAPI.SafeAddInputWithSlider(
				MOD_NAME,
				"marksmanAddGunCritRateGain",
				IS_CHINESE ? "枪手增加枪械暴击率（非加算）" : "Berserker Add GunCritRateGain",
				typeof(float),
				config.marksmanAddGunCritRateGain,
				new Vector2(0.1f, 0.5f)
			);
			ModConfigAPI.SafeAddInputWithSlider(
				MOD_NAME,
				"marksmanAddGunDamageMultiplier",
				IS_CHINESE ? "枪手增加枪械伤害倍率" : "Berserker Add GunDamageMultiplier",
				typeof(float),
				config.marksmanAddGunDamageMultiplier,
				new Vector2(0.1f, 0.5f)
			);

			// 狂战士选项
			ModConfigAPI.SafeAddInputWithSlider(
				MOD_NAME,
				"berserkerAddRunSoundRange",
				IS_CHINESE ? "狂战士增加奔跑声音距离百分比" : "Berserker Add RunSoundRangePercent",
				typeof(float),
				config.berserkerAddRunSoundRange,
				new Vector2(-0.8f, -0.1f)
			);
			ModConfigAPI.SafeAddInputWithSlider(
				MOD_NAME,
				"berserkerAddWalkSoundRange",
				IS_CHINESE ? "狂战士增加行走声音距离百分比" : "Berserker Add WalkSoundRangePercent",
				typeof(float),
				config.berserkerAddWalkSoundRange,
				new Vector2(-0.8f, -0.1f)
			);
			ModConfigAPI.SafeAddInputWithSlider(
				MOD_NAME,
				"berserkerAddMeleeCritDamageGain",
				IS_CHINESE ? "狂战士增加近战暴击伤害（非加算）" : "Berserker Add MeleeCritDamageGain",
				typeof(float),
				config.berserkerAddMeleeCritDamageGain,
				new Vector2(0.2f, 1f)
			);
			ModConfigAPI.SafeAddInputWithSlider(
				MOD_NAME,
				"berserkerAddMeleeCritRateGain",
				IS_CHINESE ? "狂战士增加近战暴击率（非加算）" : "Berserker Add MeleeCritRateGain",
				typeof(float),
				config.berserkerAddMeleeCritRateGain,
				new Vector2(0.8f, 2f)
			);
			ModConfigAPI.SafeAddInputWithSlider(
				MOD_NAME,
				"berserkerAddMeleeDamageMultiplier",
				IS_CHINESE ? "狂战士增加近战伤害倍率" : "Berserker Add MeleeDamageMultiplier",
				typeof(float),
				config.berserkerAddMeleeDamageMultiplier,
				new Vector2(0.25f, 0.5f)
			);
			ModConfigAPI.SafeAddInputWithSlider(
				MOD_NAME,
				"berserkerAddGunDamageMultiplier",
				IS_CHINESE ? "狂战士增加枪械伤害倍率" : "Berserker Add GunDamageMultiplier",
				typeof(float),
				config.berserkerAddGunDamageMultiplier,
				new Vector2(-0.9f, -0.7f)
			);
			ModConfigAPI.SafeAddInputWithSlider(
				MOD_NAME,
				"berserkerAddHeadArmor",
				IS_CHINESE ? "狂战士增加头部护甲" : "Berserker Add HeadArmor",
				typeof(float),
				config.berserkerAddHeadArmor,
				new Vector2(0f, 4f)
			);
			ModConfigAPI.SafeAddInputWithSlider(
				MOD_NAME,
				"berserkerAddBodyArmor",
				IS_CHINESE ? "狂战士增加身体护甲" : "Berserker Add BodyArmor",
				typeof(float),
				config.berserkerAddBodyArmor,
				new Vector2(0f, 4f)
			);
			ModConfigAPI.SafeAddInputWithSlider(
				MOD_NAME,
				"berserkerAddStaminaDrainRate",
				IS_CHINESE ? "狂战士增加耐力消耗" : "Berserker Add StaminaDrainRate",
				typeof(float),
				config.berserkerAddStaminaDrainRate,
				new Vector2(-2f, -1f)
			);
			ModConfigAPI.SafeAddInputWithSlider(
				MOD_NAME,
				"berserkerAddLifeStealPercent",
				IS_CHINESE ? "狂战士增加吸血百分比" : "Berserker Add LifeStealPercent",
				typeof(float),
				config.berserkerAddLifeStealPercent,
				new Vector2(15f, 40f)
			);

			// 搬运者选项
			ModConfigAPI.SafeAddInputWithSlider(
				MOD_NAME,
				"porterAddMaxWeight",
				IS_CHINESE ? "搬运者增加最大负重" : "Porter Add MaxWeight",
				typeof(float),
				config.porterAddMaxWeight,
				new Vector2(15f, 40f)
			);
			ModConfigAPI.SafeAddInputWithSlider(
				MOD_NAME,
				"porterAddRunSpeed",
				IS_CHINESE ? "搬运者增加奔跑速度" : "Porter Add RunSpeed",
				typeof(float),
				config.porterAddRunSpeed,
				new Vector2(2f, 6f)
			);
			ModConfigAPI.SafeAddInputWithSlider(
				MOD_NAME,
				"porterAddWalkSpeed",
				IS_CHINESE ? "搬运者增加行走速度" : "Porter Add WalkSpeed",
				typeof(float),
				config.porterAddWalkSpeed,
				new Vector2(2f, 6f)
			);


			// 幸存者选项
			ModConfigAPI.SafeAddInputWithSlider(
				MOD_NAME,
				"surviverAddHealthTickPercent",
				IS_CHINESE ? "幸存者持续回复百分比" : "Survivor Continuously Recover Percent",
				typeof(float),
				config.surviverAddHealthTickPercent,
				new Vector2(5f, 25f)
			);
			ModConfigAPI.SafeAddBoolDropdownList(
				MOD_NAME,
				"surviverImmuneStun",
				IS_CHINESE ? "幸存者是否免疫眩晕" : "Surviver Immune to Stun",
				config.surviverImmuneStun
			);
			ModConfigAPI.SafeAddBoolDropdownList(
				MOD_NAME,
				"surviverImmuneNauseous",
				IS_CHINESE ? "幸存者是否免疫恶心" : "Surviver Immune to Nauseous",
				config.surviverImmuneNauseous
			);
			ModConfigAPI.SafeAddBoolDropdownList(
				MOD_NAME,
				"surviverImmuneBurning",
				IS_CHINESE ? "幸存者是否免疫燃烧" : "Surviver Immune to Burning",
				config.surviverImmuneBurning
			);
			ModConfigAPI.SafeAddBoolDropdownList(
				MOD_NAME,
				"surviverImmuneElectric",
				IS_CHINESE ? "幸存者是否免疫电击" : "Surviver Immune to Electric",
				config.surviverImmuneElectric
			);
			ModConfigAPI.SafeAddBoolDropdownList(
				MOD_NAME,
				"surviverImmunePoison",
				IS_CHINESE ? "幸存者是否免疫中毒" : "Surviver Immune to Poison",
				config.surviverImmunePoison
			);
			ModConfigAPI.SafeAddBoolDropdownList(
				MOD_NAME,
				"surviverImmuneBleeding",
				IS_CHINESE ? "幸存者是否免疫流血" : "Surviver Immune to Bleeding",
				config.defaultRemoveBleeding
			);

			// 默认角色选项
			ModConfigAPI.SafeAddInputWithSlider(
				MOD_NAME,
				"defaultAddHealth",
				IS_CHINESE ? "默认角色回复生命值" : "Default Role Add Health",
				typeof(float),
				config.defaultAddHealth,
				new Vector2(10f, 40f)
			);
			ModConfigAPI.SafeAddBoolDropdownList(
				MOD_NAME,
				"defaultRemoveBleeding",
				IS_CHINESE ? "默认角色是否清除流血" : "Default Role Removes Bleed",
				config.defaultRemoveBleeding
			);

			// 通用选项
			Debug.Log($"DisplayItemValue skillCoolDown InitUI: {config.skillCoolDown}");
			ModConfigAPI.SafeAddInputWithSlider(
				MOD_NAME,
				"skillCoolDown",
				IS_CHINESE ? "技能冷却" : "Skill CD",
				typeof(int),
				config.skillCoolDown,
				new Vector2(30, 90)
			);
			ModConfigAPI.SafeAddInputWithSlider(
				MOD_NAME,
				"skillDuration",
				IS_CHINESE ? "技能持续" : "Skill Duration",
				typeof(int),
				config.skillDuration,
				new Vector2(10, 60)
			);
			SetupHotKeyConfig();
			//// 价值显示格式下拉菜单
			//var formatOptions = new SortedDictionary<string, object>
			//{
			//	{ IS_CHINESE ? "原始值" : "Raw Value", 0 },
			//	{ IS_CHINESE ? "除以2" : "Divided by 2", 1 },
			//	{ IS_CHINESE ? "除以10" : "Divided by 10", 2 }
			//};
			//Debug.Log("DisplayItemValue: ModConfig setup completed");
		}
		private void SetupHotKeyConfig()
		{
			if (!ModConfigAPI.IsAvailable())
			{
				//Debug.LogWarning("DuckovSkills: ModConfig not available for HotKey setup");
				return;
			}
			var hotkeyOptions = new SortedDictionary<string, object>();

			// --- 功能键 ---
			hotkeyOptions.Add(IS_CHINESE ? "Alt 键 (默认)" : "Alt Key (Default)", "<Keyboard>/alt");
			hotkeyOptions.Add(IS_CHINESE ? "Ctrl 键" : "Ctrl Key", "<Keyboard>/ctrl");
			hotkeyOptions.Add(IS_CHINESE ? "Shift 键" : "Shift Key", "<Keyboard>/shift");
			hotkeyOptions.Add(IS_CHINESE ? "空格键" : "Spacebar", "<Keyboard>/space");
			hotkeyOptions.Add(IS_CHINESE ? "Tab 键" : "Tab Key", "<Keyboard>/tab");
			hotkeyOptions.Add(IS_CHINESE ? "Caps Lock (大写锁定)" : "Caps Lock", "<Keyboard>/capsLock");
			hotkeyOptions.Add(IS_CHINESE ? "波浪键 (`~`)" : "Backquote Key(`~`)", "<Keyboard>/backquote");

			// --- 鼠标 ---
			hotkeyOptions.Add(IS_CHINESE ? "鼠标中键" : "Middle Mouse Button", "<Mouse>/middleButton");
			hotkeyOptions.Add(IS_CHINESE ? "鼠标侧键 (前进)" : "Mouse Forward Button", "<Mouse>/forwardButton");
			hotkeyOptions.Add(IS_CHINESE ? "鼠标侧键 (后退)" : "Mouse Back Button", "<Mouse>/backButton");

			// --- 字母键 ---
			hotkeyOptions.Add(IS_CHINESE ? "键盘 Q" : "Keyboard Q", "<Keyboard>/q");
			hotkeyOptions.Add(IS_CHINESE ? "键盘 E" : "Keyboard E", "<Keyboard>/e");
			hotkeyOptions.Add(IS_CHINESE ? "键盘 T" : "Keyboard T", "<Keyboard>/t");
			hotkeyOptions.Add(IS_CHINESE ? "键盘 Y" : "Keyboard Y", "<Keyboard>/y");
			hotkeyOptions.Add(IS_CHINESE ? "键盘 F" : "Keyboard F", "<Keyboard>/f");
			hotkeyOptions.Add(IS_CHINESE ? "键盘 G" : "Keyboard G", "<Keyboard>/g");
			hotkeyOptions.Add(IS_CHINESE ? "键盘 Z" : "Keyboard Z", "<Keyboard>/z");
			hotkeyOptions.Add(IS_CHINESE ? "键盘 X" : "Keyboard X", "<Keyboard>/x");
			hotkeyOptions.Add(IS_CHINESE ? "键盘 C" : "Keyboard C", "<Keyboard>/c");
			hotkeyOptions.Add(IS_CHINESE ? "键盘 V" : "Keyboard V", "<Keyboard>/v");
			hotkeyOptions.Add(IS_CHINESE ? "键盘 B" : "Keyboard B", "<Keyboard>/b");

			// --- 数字键 ---
			hotkeyOptions.Add(IS_CHINESE ? "数字键 1" : "Num 1", "<Keyboard>/1");
			hotkeyOptions.Add(IS_CHINESE ? "数字键 2" : "Num 2", "<Keyboard>/2");
			hotkeyOptions.Add(IS_CHINESE ? "数字键 3" : "Num 3", "<Keyboard>/3");
			hotkeyOptions.Add(IS_CHINESE ? "数字键 4" : "Num 4", "<Keyboard>/4");
			hotkeyOptions.Add(IS_CHINESE ? "数字键 5" : "Num 5", "<Keyboard>/5");
			hotkeyOptions.Add(IS_CHINESE ? "数字键 6" : "Num 6", "<Keyboard>/6");

			// --- F功能键 ---
			hotkeyOptions.Add(IS_CHINESE ? "F1 键" : "F1", "<Keyboard>/f1");
			hotkeyOptions.Add(IS_CHINESE ? "F2 键" : "F2", "<Keyboard>/f2");
			hotkeyOptions.Add(IS_CHINESE ? "F3 键" : "F3", "<Keyboard>/f3");
			hotkeyOptions.Add(IS_CHINESE ? "F4 键" : "F4", "<Keyboard>/f4");


			// 2. 调用 SafeAddDropdownList 来创建下拉菜单
			ModConfigAPI.SafeAddDropdownList(
				MOD_NAME,
				"hotKey",
				IS_CHINESE ? "技能快捷键" : "Skill Hotkey",
				hotkeyOptions,
				typeof(string),
				config.hotKey
			);
		}
		private void UpdateHotkey()
		{
			if (activateSkillAction != null)
			{
				if (activateSkillAction.bindings[0].effectivePath == config.hotKey)
				{
					Debug.Log($"DuckovSkills no need update hotKey: '{config.hotKey}'");
					return;
				}
				activateSkillAction.performed -= OnActivateSkill;
				activateSkillAction.Disable();
				activateSkillAction.Dispose(); // 销毁旧对象，防止内存泄漏
				activateSkillAction = null;
			}

			Debug.Log($"DuckovSkills: Attempting to set hotKey to: '{config.hotKey}'");
			try
			{
				activateSkillAction = new InputAction("ActivateSkill", InputActionType.Button, config.hotKey);
			}
			catch (Exception ex)
			{
				Debug.LogError($"DuckovSkills: Invalid hotKey format '{config.hotKey}'. Reverting to default '<Keyboard>/alt'. Error: {ex.Message}");
				activateSkillAction = new InputAction("ActivateSkill", InputActionType.Button, "<Keyboard>/alt");
			}
			activateSkillAction.performed += OnActivateSkill;
			activateSkillAction.Enable();

			Debug.Log($"DuckovSkills: hotKey successfully bound to '{activateSkillAction.bindings[0].effectivePath}'");
		}
		private void OnModConfigOptionsChanged(string key)
		{
			if (!key.StartsWith(MOD_NAME + "_"))
				return;

			// 使用新的 LoadConfig 方法读取配置
			LoadConfigFromModConfig();
			// 保存到本地配置文件
			SaveConfig(config);
			//Debug.Log($"DisplayItemValue: ModConfig updated - {key}");
		}
		private void LoadConfigFromModConfig()
		{
			// 使用新的 LoadConfig 方法读取所有配置
			config.hotKey = ModConfigAPI.SafeLoad<string>(MOD_NAME, "hotKey", config.hotKey);
			Debug.Log($"DisplayItemValue HotKey: {config.hotKey}");
			UpdateHotkey();
			//Debug.Log($"DisplayItemValue skillCoolDown PreLoad: {config.skillCoolDown}");
			config.skillCoolDown = ModConfigAPI.SafeLoad<int>(MOD_NAME, "skillCoolDown", config.skillCoolDown);
			//Debug.Log($"DisplayItemValue skillCoolDown AfterLoad: {config.skillCoolDown}");
			config.skillDuration = ModConfigAPI.SafeLoad<int>(MOD_NAME, "skillDuration", config.skillDuration);

			config.defaultRemoveBleeding = ModConfigAPI.SafeLoad<bool>(MOD_NAME, "defaultRemoveBleeding", config.defaultRemoveBleeding);
			config.defaultAddHealth = ModConfigAPI.SafeLoad<float>(MOD_NAME, "defaultAddHealth", config.defaultAddHealth);

			config.surviverImmuneBleeding = ModConfigAPI.SafeLoad<bool>(MOD_NAME, "surviverImmuneBleeding", config.surviverImmuneBleeding);
			config.surviverImmunePoison = ModConfigAPI.SafeLoad<bool>(MOD_NAME, "surviverImmunePoison", config.surviverImmunePoison);
			config.surviverImmuneElectric = ModConfigAPI.SafeLoad<bool>(MOD_NAME, "surviverImmuneElectric", config.surviverImmuneElectric);
			config.surviverImmuneBurning = ModConfigAPI.SafeLoad<bool>(MOD_NAME, "surviverImmuneBurning", config.surviverImmuneBurning);
			config.surviverImmuneNauseous = ModConfigAPI.SafeLoad<bool>(MOD_NAME, "surviverImmuneNauseous", config.surviverImmuneNauseous);
			config.surviverImmuneStun = ModConfigAPI.SafeLoad<bool>(MOD_NAME, "surviverImmuneStun", config.surviverImmuneStun);
			config.surviverAddHealthTickPercent = ModConfigAPI.SafeLoad<float>(MOD_NAME, "surviverAddHealthTickPercent", config.surviverAddHealthTickPercent);

			config.porterAddWalkSpeed = ModConfigAPI.SafeLoad<float>(MOD_NAME, "porterAddWalkSpeed", config.porterAddWalkSpeed);
			config.porterAddRunSpeed = ModConfigAPI.SafeLoad<float>(MOD_NAME, "porterAddRunSpeed", config.porterAddRunSpeed);
			config.porterAddMaxWeight = ModConfigAPI.SafeLoad<float>(MOD_NAME, "porterAddMaxWeight", config.porterAddMaxWeight);

			config.berserkerAddLifeStealPercent = ModConfigAPI.SafeLoad<float>(MOD_NAME, "berserkerAddLifeStealPercent", config.berserkerAddLifeStealPercent);
			config.berserkerAddStaminaDrainRate = ModConfigAPI.SafeLoad<float>(MOD_NAME, "berserkerAddStaminaDrainRate", config.berserkerAddStaminaDrainRate);
			config.berserkerAddBodyArmor = ModConfigAPI.SafeLoad<float>(MOD_NAME, "berserkerAddBodyArmor", config.berserkerAddBodyArmor);
			config.berserkerAddHeadArmor = ModConfigAPI.SafeLoad<float>(MOD_NAME, "berserkerAddHeadArmor", config.berserkerAddHeadArmor);
			config.berserkerAddGunDamageMultiplier = ModConfigAPI.SafeLoad<float>(MOD_NAME, "berserkerAddGunDamageMultiplier", config.berserkerAddGunDamageMultiplier);
			config.berserkerAddMeleeDamageMultiplier = ModConfigAPI.SafeLoad<float>(MOD_NAME, "berserkerAddMeleeDamageMultiplier", config.berserkerAddMeleeDamageMultiplier);
			config.berserkerAddMeleeCritRateGain = ModConfigAPI.SafeLoad<float>(MOD_NAME, "berserkerAddMeleeCritRateGain", config.berserkerAddMeleeCritRateGain);
			config.berserkerAddMeleeCritDamageGain = ModConfigAPI.SafeLoad<float>(MOD_NAME, "berserkerAddMeleeCritDamageGain", config.berserkerAddMeleeCritDamageGain);
			config.berserkerAddWalkSoundRange = ModConfigAPI.SafeLoad<float>(MOD_NAME, "berserkerAddWalkSoundRange", config.berserkerAddWalkSoundRange);
			config.berserkerAddRunSoundRange = ModConfigAPI.SafeLoad<float>(MOD_NAME, "berserkerAddRunSoundRange", config.berserkerAddRunSoundRange);

			config.marksmanAddGunDamageMultiplier = ModConfigAPI.SafeLoad<float>(MOD_NAME, "marksmanAddGunDamageMultiplier", config.marksmanAddGunDamageMultiplier);
			config.marksmanAddGunCritRateGain = ModConfigAPI.SafeLoad<float>(MOD_NAME, "marksmanAddGunCritRateGain", config.marksmanAddGunCritRateGain);
			config.marksmanAddGunCritDamageGain = ModConfigAPI.SafeLoad<float>(MOD_NAME, "marksmanAddGunCritDamageGain", config.marksmanAddGunCritDamageGain);
			config.marksmanAddGunDistanceMultiplier = ModConfigAPI.SafeLoad<float>(MOD_NAME, "marksmanAddGunDistanceMultiplier", config.marksmanAddGunDistanceMultiplier);
			config.marksmanAddGunScatterMultiplier = ModConfigAPI.SafeLoad<float>(MOD_NAME, "marksmanAddGunScatterMultiplier", config.marksmanAddGunScatterMultiplier);
			config.marksmanAddRecoilControl = ModConfigAPI.SafeLoad<float>(MOD_NAME, "marksmanAddRecoilControl", config.marksmanAddRecoilControl);
		}
		private void SaveConfig(DisplaySkillValueConfig config)
		{
			try
			{
				string json = JsonUtility.ToJson(config, true);
				File.WriteAllText(persistentConfigPath, json);
				Debug.Log("DisplaySkill: Config saved");
			}
			catch (Exception e)
			{
				Debug.LogError($"DisplaySkill: Failed to save config: {e}");
			}
		}

	}
}