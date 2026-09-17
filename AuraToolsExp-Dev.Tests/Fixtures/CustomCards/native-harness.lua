-- Minimal native API boundary for executable compiler tests; no alternate card interpreter.
function List(items)
  local t={items=items or {},kind='IDataConfigList'}
  setmetatable(t,{__index=function(s,k) if k=='Count' then return #s.items end end})
  function t:get_Item(i) return self.items[i+1] end
  function t:Add(v) self.items[#self.items+1]=v end
  function t:Clear() self.items={} end
  function t:Remove(v) for i,x in ipairs(self.items) do if x==v then table.remove(self.items,i);return true end end;return false end
  function t:GetType() return self.kind end
  return t
end
own={CurHp=25,MaxHp=50,Defend=8,state='Default',InstanceId='self'}
enemy={CurHp=30,MaxHp=30,Defend=0,state='Default',InstanceId='enemy'}
function own:GetBuff(id) return nil end
function enemy:GetBuff(id) return nil end
card={}
CS={FightManager={Instance={fightType='Player'}},FightPlayer={Instance={Status=own,CurPowerCount=3,MaxPowerCount=3}},FightCardManager={Instance={cardList=List({card}),usedCardList=List()}},System={Activator={CreateInstance=function(kind) assert(kind=='IDataConfigList');return List() end}},ScriptExecutor={PlayerInfo={CardList=List()}}}
self={Self=own,Target=enemy,Object=List({enemy}),status=nil,HandCard=List(),DeckCard=List({card}),UsedCard=List(),dataConfig={data={get_Item=function(_,k) return '1' end}}}
function self:SetStatus(filter) self.Object:Clear();if filter=='Self' then self.Object:Add(own) elseif filter=='AllFriends' or filter=='AllRandomFriends1' then self.Object:Add(own) elseif filter=='All' then self.Object:Add(own);self.Object:Add(enemy) else self.Object:Add(enemy) end;return self.Object end
function self:Damage(amount,kind) for _,o in ipairs(self.Object.items) do o.CurHp=o.CurHp-tonumber(amount) end;self.status=nil end
function self:ChangeDefence(amount) for _,o in ipairs(self.Object.items) do o.Defend=o.Defend+tonumber(amount) end end
function self:ChangeHp(amount) for _,o in ipairs(self.Object.items) do o.CurHp=math.min(o.MaxHp,o.CurHp+tonumber(amount)) end end
function self:ChangeMaxHp(amount) for _,o in ipairs(self.Object.items) do o.MaxHp=o.MaxHp+tonumber(amount) end end
function self:ChangePower(amount) CS.FightPlayer.Instance.CurPowerCount=CS.FightPlayer.Instance.CurPowerCount+tonumber(amount) end
function self:GetDeckUIToAction(count,source,callback) assert(source.kind=='IDataConfigList');pending=callback end
function self:CreateCard(card) self.HandCard:Add(card) end
events={}
function self:AddEvent(event,callback) events[event]=events[event] or {};table.insert(events[event],callback) end
fightui={createCardQueue=List()}
function fightui:GetType() return {FullName='Witch.UI.Window.FightUI'} end
CS.Witch={UI={UIManager={Instance={}}}}
function CS.Witch.UI.UIManager.Instance:GetAllUI()
  local list={}
  function list:GetEnumerator()
    local e={Current=fightui,done=false}
    function e:MoveNext() if self.done then return false end;self.done=true;return true end
    function e:Dispose() end
    return e
  end
  return list
end
