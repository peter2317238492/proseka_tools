我看过 D:\mitm\suite.json，这是一个很大的游戏用户数据快照。下面按“顶层字段”给出含义和结构说明（基于字段名推断，
  少数游戏专有名词会有不确定性）。如果你要“逐条到每个列表元素/字段级别”的解释，请告诉我具体 key，我再往下拆。    
                                                                                                                 
  补充：compactUser* 这种是“列式存储”，同一索引位置组成一条记录；__ENUM__ 提供枚举值。                           
                                                                                                                 
  compactUserCharacterMissionV2Statuses: 角色任务V2状态（列式；missionId/missionStatus/characterId/seq/          
  parameterGroupId）                                                                                             
  compactUserCostume3dShopItems: 3D服装商店条目状态（列式；含status）                                            
  compactUserCostume3dStatuses: 3D服装获得时间（列式）                                                           
  compactUserMissionStatuses: 任务状态（列式；missionType/missionStatus）                                        
  compactUserMusicAchievements: 乐曲成就（列式）                                                                 
  compactUserMusicResults: 乐曲成绩（列式；难度、分数、FC等）                                                    
  now: 当前时间戳                                                                                                
  refreshableTypes: 可刷新类型列表                                                                               
                                                                                                                 
  userRegistration: 注册信息（平台/设备/系统/注册时间）                                                          
  userGamedata: 账号基础信息与进度（昵称/等级/经验/卡组等）                                                      
  userChargedCurrency: 货币（付费/免费）                                                                         
  userBoost: 体力/boost 当前值与恢复时间                                                                         
  userConfig: 用户设置                                                                                           
  userTutorial: 新手引导状态                                                                                     
  userAreas: 区域/场景状态与行动                                                                                 
  userActionSets: 行动集状态                                                                                     
  userCards: 已拥有卡牌（等级/技能/经验等）                                                                      
  userDecks: 编队                                                                                                
  userMusics: 曲目解锁与难度状态
  userShops: 商店状态                                                                                            
  userBillingShopItems: 充值商店商品购买状态                                                                     
  userColorfulPassV2: 通行证状态
  userPracticeTickets: 练习票数量                                                                                
  userSkillPracticeTickets: 技能练习票数量                                                                       
  userMaterials: 材料数量                                                                                        
  userGachas: 抽卡记录/次数                                                                                      
  userGachaBonusPoints: 抽卡点数                                                                                 
  userUnitEpisodeStatuses: 组合剧情章节状态                                                                      
  userSpecialEpisodeStatuses: 特别剧情状态                                                                       
  userEventEpisodeStatuses: 活动剧情状态                                                                         
  userArchiveEventEpisodeStatuses: 活动回放剧情状态                                                              
  userCharacterProfileEpisodeStatuses: 角色档案剧情状态                                                          
  userStoryMission: 主线任务进度                                                                                 
  userEventArchiveCompleteReadRewards: 活动回放阅读奖励进度                                                      
  userUnits: 组合等级/经验                                                                                       
  userPresents: 礼物箱                                                                                           
  userCharacterCostume3ds: 角色3D服装装配                                                                        
  userReleaseConditions: 解锁条件达成记录
  unreadUserTopics: 未读公告                                                                                     
  userHomeBanners: 首页 banner 状态                                                                              
  userStamps: 表情/贴图                                                                                          
  userStampFavorites: 常用贴图                                                                                   
  userStampFavoriteTabs: 常用贴图分页                                                                            
  userMaterialExchanges: 材料兑换记录                                                                            
  userGachaCeilExchanges: 天井兑换状态                                                                           
  userGachaCeilItems: 天井道具数量                                                                               
  userGachaTickets: 抽卡券数量                                                                                   
  userBoostItems: 体力道具数量                                                                                   
  userCharacters: 角色等级/经验                                                                                  
  userCharacterMissionV2s: 角色任务V2进度                                                                        
  userBonds: 羁绊等级/经验                                                                                       
  userNormalMissions: 普通任务                                                                                   
  userBeginnerMissions: 新手任务                                                                                 
  userBeginnerMissionV2s: 新手任务V2                                                                             
  userBeginnerMissionBehavior: 新手任务行为/状态                                                                 
  userLiveMissions: Live 任务进度                                                                                
  userFixCostumes: 服装修复/调整相关（推测）                                                                     
  userProfile: 个人资料                                                                                          
  userHonors: 称号/徽章                                                                                          
  userHonorMissions: 称号任务进度                                                                                
  userProfileHonors: 个人资料展示称号                                                                            
  userBondsHonors: 羁绊称号                                                                                      
  userBondsHonorWords: 羁绊称号文案                                                                              
  userChallengeLivePlayDay: 挑战 Live 每日次数/重置                                                              
  userChallengeLivePlayStatuses: 挑战 Live 状态                                                                  
  userChallengeLiveSoloDecks: 挑战 Live 单人编队                                                                 
  userChallengeLiveSoloResults: 挑战 Live 成绩                                                                   
  userChallengeLiveSoloStages: 挑战 Live 关卡状态                                                                
  userChallengeLiveSoloHighScoreRewards: 挑战 Live 高分奖励状态                                                  
  userVirtualLiveBeginnerScheduleStatuses: 新手虚拟 Live 状态                                                    
  userVirtualLiveScheduleStatuses: 虚拟 Live 日程状态                                                            
  userArchiveVirtualLiveStatuses: 虚拟 Live 回放状态                                                             
  userVirtualLiveRewards: 虚拟 Live 奖励领取                                                                     
  userVirtualShops: 虚拟商店                                                                                     
  userVirtualLiveTickets: 虚拟 Live 门票                                                                         
  userVirtualLivePamphlets: 虚拟 Live 小册子                                                                     
  userAvatar: 头像当前装扮                                                                                       
  userAvatarAccessories: 头像配件                                                                                
  userAvatarCostumes: 头像服装                                                                                   
  userAvatarMotions: 头像动作                                                                                    
  userAvatarMotionFavorites: 常用动作                                                                            
  userAvatarSkinColors: 头像肤色                                                                                 
  userAvatarCoordinates: 头像套装                                                                                
  userPenlights: 荧光棒                                                                                          
  userLoginBonuses: 登录奖励进度                                                                                 
  userCharacterLiveUsageCounts: 角色使用次数统计                                                                 
  userOneTimeBehaviors: 一次性行为/标记                                                                          
  userEvents: 活动进度/积分                                                                                      
  userEventItems: 活动道具数量                                                                                   
  userEventExchanges: 活动兑换记录                                                                               
  userCheerfulCarnivals: Cheerful Carnival 相关（空）                                                            
  userCheerfulCarnivalBehaviors: 同上（空）                                                                      
  userMultiLivePenalty: 多人房处罚状态                                                                           
  userAutoLive: 自动 Live 次数                                                                                   
  userFriends: 好友列表                                                                                          
  userBlocks: 黑名单
  userGachaWishes: 抽卡心愿                                                                                      
  userGiftGachaWishes: 礼物抽卡心愿                                                                              
  userCategorizedGachaWishes: 分类心愿                                                                           
  userBoostGranteds: 体力发放记录                                                                                
  userBoostReceived: 体力已领取标记（空对象）                                                                    
  userCheerfulCarnivalResultRewards: Cheerful Carnival 奖励（空）                                                
  userGachaCeilExchangeSubstituteCosts: 天井替代成本                                                             
  userCustomProfiles: 自定义名片                                                                                 
  userCustomProfileCards: 名片卡片                                                                               
  userCustomProfileResources: 名片资源                                                                           
  userCustomProfileGachas: 名片相关扭蛋（空）                                                                    
  userRankMatchResult: 排位赛结果                                                                                
  userRankMatchSeasons: 排位赛季状态                                                                             
  userPanelMissionCampaigns: 面板任务活动                                                                        
  userEventMissions: 活动任务进度                                                                                
  userMyLists: 自定义歌单                                                                                        
  userPaidVirtualLives: 付费虚拟 Live 购买                                                                       
  userPaidVirtualLiveStatuses: 付费 Live 状态（空）                                                              
  userPaidVirtualLiveShopItems: 付费 Live 商店                                                                   
  userGachaFreeResources: 免费抽卡资源冷却                                                                       
  userStoryFavorites: 收藏剧情                                                                                   
  userBookmarkedStories: 书签剧情（空）                                                                          
  userFriendInvitationCampaigns: 邀请活动                                                                        
  userFriendInvitationCampaignMissionRewardCounts: 邀请任务奖励次数（空）                                        
  userWorldBlooms: World Bloom 活动进度                                                                          
  userWorldBloomSupportDecks: World Bloom 支援编队                                                               
  userLiveCharacterArchiveVoice: 角色语音档案解锁                                                                
  userAdRewards: 广告奖励（空）                                                                                  
  userAppeals: 公告/提示阅读                                                                                     
  userViewableAppeal: 可查看的公告                                                                               
  newReleaseConditions: 新解锁条件                                                                               
  userOmikujis: 御神签/抽签（空）                                                                                
  userMysekaiMaterials: Mysekai 材料                                                                             
  userMysekaiCanvases: Mysekai 画布/装饰                                                                         
  userMysekaiGamedata: Mysekai 总体进度                                                                          
  userMysekaiGates: Mysekai Gate                                                                                 
  userMysekaiCharacterTalks: Mysekai 对话阅读                                                                    
  userMysekaiColorfulPass: Mysekai 通行证                                                                        
  userMysekaiFixtureGameCharacterPerformanceBonuses: Mysekai 设施加成                                            
  bgipRedDotCount: 红点提示数量                                                                                  
  friendShare: 好友分享奖励状态                                                                                  
  userLimitMissions: 限时任务                                                                                    
  userReturnMissions: 回归任务（空）                                                                             
  userSudokuMissionTurns: 数独任务回合（推测）                                                                   
  userSudokuMissions: 数独任务进度（推测）                                                                       
  eventShowDialogueTabs: 活动对话标签                                                                            
  firstRecharge: 首充标记/次数（推测）                                                                           
  userShiningExchanges: Shining 兑换                                                                             
  userOngoingMissions: 进行中的任务                                                                              
  userSekaiEchoMissions: Sekai Echo 任务                                                                         
  userSekaiEchoCards: Sekai Echo 卡牌                                                                            
  userSekaiEchoCardMissions: Sekai Echo 卡牌任务                                                                 
  userSekaiEchoHonors: Sekai Echo 称号/等级
  userSekaiEchoHonorMissions: Sekai Echo 称号任务
  userNoticePopups: 弹窗通知（空）