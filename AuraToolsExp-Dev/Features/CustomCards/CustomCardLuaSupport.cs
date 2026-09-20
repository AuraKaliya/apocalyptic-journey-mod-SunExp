namespace AuraToolsExp.Dll.Features.CustomCards;

internal static class CustomCardLuaSupport
{
    // Shared by production generation and executable Lua behavior tests. No preview-only backend.
    internal const string Source = @"-- AuraTools.CustomCard compiler 5
local __self = self
local __f = {}
local __runtime = rawget(_G, '__AuraCustomCardV2')
if not __runtime then __runtime=setmetatable({}, {__mode='v'}); rawset(_G,'__AuraCustomCardV2',__runtime) end
local function __number(v)
  local n = tonumber(v)
  assert(n and n == n and math.abs(n) <= 1000000, '自建卡牌：数值无效或超出范围')
  return n
end
local function __divide(a,b) assert(b ~= 0, '自建卡牌：除数为零'); return __number(a/b) end
local function __clamp(a,l,h) assert(l <= h, '自建卡牌：范围上下限颠倒'); return math.max(l, math.min(a,h)) end
local function __near(a,b,t) assert(t >= 0, '自建卡牌：容差不能为负数'); return math.abs(a-b) <= t end
local function __alive(o) return o ~= nil and tostring(o.state) ~= 'Dead' end
local function __one(o) if __alive(o) then return {o} end; return {} end
local function __copy(xs)
  local t = {}; for i=0,xs.Count-1 do t[#t+1] = xs[i] end; return t
end
local function __restore(xs, status, target)
  __self.Object:Clear(); for _,o in ipairs(xs) do __self.Object:Add(o) end
  __self.status = status; __self.Target = target
end
local function __with(objects, action)
  local xs, status, target = __copy(__self.Object), __self.status, __self.Target
  __self.Object:Clear(); for _,o in ipairs(objects) do if __alive(o) then __self.Object:Add(o) end end
  __self.status = nil
  local ok, err = pcall(action)
  __restore(xs,status,target)
  if not ok then error(err) end
end
local function __objects(kind)
  local filters = {Enemies='AllTarget',Friends='AllFriends',All='All'}
  local result
  __with({},function() result=__copy(__self:SetStatus(filters[kind])) end)
  return result
end
local function __context(trigger)
  local battle=CS.FightManager.Instance
  local budget=__runtime.current or (trigger~='Use' and __runtime.pending or nil)
  if not budget or budget.battle~=battle then budget={steps=0,entries=0,depth=0,battle=battle} end
  budget.entries=budget.entries+1
  if budget.stopped or budget.entries>128 then budget.stopped=true;error('自建卡牌：连锁触发超过限制') end
  return {vars={},target=trigger=='Use' and __self.Target or nil,budget=budget,battle=battle,life={finished=false,cleanup={}}}
end
local function __child(c)
  local v={}; for k,x in pairs(c.vars) do v[k]=x end
  return {vars=v,target=c.target,current=c.current,budget=c.budget,battle=c.battle,life=c.life}
end
local function __finish(c)
  if c.life.finished then return end
  c.life.finished=true
  for _,release in ipairs(c.life.cleanup) do release() end
  c.life.cleanup={}
end
local function __run(c, action)
  if c.life.finished then return end
  local previous=__runtime.current; __runtime.current=c.budget
  c.budget.depth=c.budget.depth+1
  local ok,result=pcall(function()
    assert(c.budget.depth<=64 and not c.budget.stopped,'自建卡牌：连锁深度超过限制')
    return action()
  end)
  c.budget.depth=c.budget.depth-1; __runtime.current=previous
  if not ok then c.budget.stopped=true;__finish(c);error(tostring(result)..'; 节点 '..tostring(c.life.node or c.node or '入口')) end
  return result
end
local function __active(c)
  local f=CS.FightManager.Instance
  return not c.life.finished and not c.budget.stopped and f ~= nil and f == c.battle and tostring(f.fightType) ~= 'None' and tostring(f.fightType) ~= 'Win' and tostring(f.fightType) ~= 'Loss' and __alive(__self.Self)
end
local function __value(c,id,read)
  c.budget.steps=c.budget.steps+1
  assert(c.budget.steps<=4096,'自建卡牌：数据求值超过执行预算；节点 '..id)
  local previous=c.life.node
  if id~='' then c.life.node=id end
  local value=read()
  if type(value)=='number' then value=__number(value) end
  c.life.node=previous
  return value
end
local function __tick(c,id)
  c.node=id; c.life.node=id; c.budget.steps=c.budget.steps+1
  assert(c.budget.steps<=4096, '自建卡牌：执行步骤超过 4096；积木 '..id)
end
local function __read(o,field,buff)
  assert(__alive(o), '自建卡牌：读取的对象已不存在')
  if field=='Health' then return o.CurHp end
  if field=='MaxHealth' then return o.MaxHp end
  if field=='MissingHealth' then return math.max(0,o.MaxHp-o.CurHp) end
  if field=='HealthPercent' then if o.MaxHp<=0 then return 0 end; return o.CurHp/o.MaxHp end
  if field=='Shield' then return o.Defend end
  if field=='BuffStacks' then local v=o:GetBuff(buff); if v==nil then return 0 end; return v.buffConfig.Level end
  local p=CS.FightPlayer.Instance
  assert(p~=nil and o==p.Status, '自建卡牌：这个数据只属于当前玩家')
  if field=='Energy' then return p.CurPowerCount end
  if field=='MaxEnergy' then return p.MaxPowerCount end
  if field=='HandCount' then return __self.HandCard.Count end
  if field=='DeckCount' then return __self.DeckCard.Count end
  if field=='DiscardCount' then return __self.UsedCard.Count end
  if field=='CardCost' then return __number(__self.dataConfig.data:get_Item('Expend')) end
  error('自建卡牌：未知战斗数据')
end
local function __sequence(c, fs, done)
  local i=0
  local function nextStep()
    if not __active(c) then return __finish(c) end
    i=i+1; if i>#fs then return done() end
    return fs[i](c,nextStep)
  end
  return nextStep()
end
local function __repeat(c,n,body,done)
  n=__number(n); assert(n==math.floor(n) and n>=0 and n<=64, '自建卡牌：重复次数需为 0～64 整数')
  local i=0
  local function nextStep()
    if not __active(c) then return __finish(c) end
    i=i+1; if i>n then return done() end
    __tick(c,c.node or '重复')
    return body(__child(c),nextStep)
  end
  return nextStep()
end
local function __each(c,objects,predicate,body,done)
  local i=0
  local function nextStep()
    if not __active(c) then return __finish(c) end
    i=i+1
    while i<=#objects do
      __tick(c,c.node or '遍历')
      local child=__child(c); child.current=objects[i]
      if __alive(child.current) and predicate(child) then return body(child,nextStep) end
      i=i+1
    end
    return done()
  end
  return nextStep()
end
local function __pick(c,id,objects,body,empty,done)
  local candidates={}
  for _,o in ipairs(objects) do if __alive(o) then candidates[#candidates+1]=o end end
  if #candidates==0 then return empty(__child(c),done) end
  local index=__self.DefaultDice:WithRange(0,#candidates-1):Roll().Value+1
  assert(index>=1 and index<=#candidates,'自建卡牌：原生随机结果越界')
  local child=__child(c);child.vars[id]=candidates[index]
  return body(child,done)
end
local function __waitHand(c,done)
  local iterator=CS.Witch.UI.UIManager.Instance:GetAllUI():GetEnumerator()
  local ui=nil
  while iterator:MoveNext() do
    local candidate=iterator.Current
    if candidate:GetType().FullName=='Witch.UI.Window.FightUI' then ui=candidate;break end
  end
  iterator:Dispose()
  assert(ui~=nil,'自建卡牌：战斗界面已关闭')
  if ui.createCardQueue.Count==0 then return done() end
  local finished=false
  local center=CS.EventCenter.Instance
  local owner=CS.System.Object()
  local event='EndCreateCardItem'..__self.Self.InstanceId
  __runtime.pending=c.budget
  c.budget.waits=(c.budget.waits or 0)+1
  local function release()
    if finished then return end;finished=true
    center:RemoveEventListener(event,owner)
    c.budget.waits=c.budget.waits-1
    if c.budget.waits==0 and __runtime.pending==c.budget then __runtime.pending=nil end
  end
  table.insert(c.life.cleanup,release)
  center:AddEventListener(event,function()
    if finished then return end
    if not __active(c) then return __finish(c) end
    if ui.createCardQueue.Count==0 then release();return __run(c,done) end
  end,owner,CS.EventDispose.OnFightEnd)
end
local function __effect(c,kind,objects,amount,buff,done)
  if #objects==0 then return done() end
  amount=__number(amount)
  assert(amount>=0, '自建卡牌：效果数量不能为负数；积木 '..c.node)
  amount=math.floor(amount)
  if kind=='Draw' or kind=='Discard' or kind=='Burn' or kind=='SelectFromDeck' or kind=='SelectFromDiscard' then
    assert(amount<=64,'自建卡牌：单次牌堆操作最多 64 张')
  end
  if kind=='SelectFromDeck' or kind=='SelectFromDiscard' then
    if amount==0 then return done() end
    local original=kind=='SelectFromDeck' and __self.DeckCard or __self.UsedCard
    if original.Count==0 then return done() end
    -- The native overload requires List<IDataConfig>, not List<DataConfig>.
    local source=CS.System.Activator.CreateInstance(CS.ScriptExecutor.PlayerInfo.CardList:GetType())
    for i=0,original.Count-1 do source:Add(original[i]) end
    local chosen=false
    __self:GetDeckUIToAction(tostring(math.min(amount,source.Count)),source,function(cards)
      if chosen then return end
      if not __active(c) then return __finish(c) end
      chosen=true
      if cards.Count==0 then return __finish(c) end
      return __run(c,function()
      local manager=CS.FightCardManager.Instance
      for i=0,cards.Count-1 do
        local card=cards[i]
        manager.cardList:Remove(card); manager.usedCardList:Remove(card)
        __self:CreateCard(card)
      end
      return __waitHand(c,done)
      end)
    end)
    return
  end
  if kind=='Discard' or kind=='Burn' then
    if amount==0 or __self.HandCard.Count==0 then return done() end
    local finished=false
    __with(__one(__self.Self),function()
    __self:ChooseCardToAction(tostring(amount),function(cards)
      if finished then return end
      if not __active(c) then return __finish(c) end
      finished=true
      if cards.Count==0 then return __finish(c) end
      return __run(c,function()
      for i=0,cards.Count-1 do
        local card=cards[i]
        if kind=='Discard' then card:InternalThrow() else card:InternalBurning() end
      end
      return done()
      end)
    end,'0')
    end)
    return
  end
  __with(objects,function()
    local v=tostring(amount)
    if kind=='Damage' then __self:Damage(v,'Normal')
    elseif kind=='TrueDamage' then __self:Damage(v,'True')
    elseif kind=='Shield' then __self:ChangeDefence(v)
    elseif kind=='Heal' then __self:ChangeHp(v)
    elseif kind=='MaxHealth' then __self:ChangeMaxHp(v)
    elseif kind=='Energy' then __self:ChangePower(v)
    elseif kind=='Draw' then __self:DrawCount(v)
    elseif kind=='AddBuff' then __self:AddBuff(buff,v)
    elseif kind=='RemoveBuff' then __self:RemoveBuff(buff)
    elseif kind=='Shuffle' then __self:ShuffleDeck()
    elseif kind=='EndTurn' then __self:ChangeRound()
    else error('自建卡牌：未知效果') end
  end)
  if kind=='Draw' then return __waitHand(c,done) end
  if kind=='EndTurn' then return __finish(c) end
  return done()
end
";
}
