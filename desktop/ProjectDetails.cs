using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Cofoundry {
public partial class MainForm {
  TableLayoutPanel detailColumns;
  Panel detailHistoryPane;
  bool? detailHistoryOverride;
  void LayoutDetailColumns(){
    if(detailColumns==null||detailColumns.IsDisposed||detailHistoryPane==null)return;
    bool show=detailHistoryOverride??(detailWorkspace.ClientSize.Width>=Dpx(1180));
    detailColumns.SuspendLayout();
    detailColumns.ColumnStyles[0].SizeType=SizeType.Absolute;
    detailColumns.ColumnStyles[0].Width=Math.Min(Dpx(270),Math.Max(Dpx(210),detailWorkspace.ClientSize.Width*.22F));
    detailColumns.ColumnStyles[1].SizeType=SizeType.Percent;detailColumns.ColumnStyles[1].Width=100;
    detailColumns.ColumnStyles[2].SizeType=SizeType.Absolute;detailColumns.ColumnStyles[2].Width=show?Math.Min(Dpx(280),detailWorkspace.ClientSize.Width*.24F):0;
    detailHistoryPane.Visible=show;detailColumns.ResumeLayout();FitDetailFlow(detailLeft);FitDetailFlow(detailHistory);LayoutDetailMessages();
  }
  Panel detailWorkspace,detailCenter;
  FlowLayoutPanel detailLeft,detailMessages,detailHistory,detailPlanPanel;
  TextBox detailInput;
  Label detailHeading,detailSubtitle,detailProgress,detailSendTo,detailHistoryCount;
  Button detailSend,detailPause;
  ComboBox detailHistoryFilter;
  string detailProjectId,detailTarget="",detailSnapshot,detailHistorySnapshot,detailPlanSnapshot;
  bool detailUpdating,detailSending;
  readonly Dictionary<string,string> detailTargets=new Dictionary<string,string>();
  readonly Dictionary<string,string> detailDrafts=new Dictionary<string,string>();
  readonly Dictionary<string,Button> detailPeople=new Dictionary<string,Button>();
  string DepartmentName(Agent a){return String.IsNullOrWhiteSpace(a.department)?Ui.T("未设置职能部门"):a.department.Trim();}
  int Dpx(int value){return (int)Math.Round(value*uiScale);}
  string DraftKey(){return detailProjectId+"/"+detailTarget;}
  string CommandStatus(string status){switch(status){case "queued":return Ui.T("排队中");case "running":return Ui.T("正在回复");case "done":return Ui.T("已回复");case "failed":return Ui.T("未完成");default:return status;}}
  string ShortText(string text,int count){text=(text??"").Replace("\r","").Replace("\n"," ");return text.Length>count?text.Substring(0,count)+"…":text;}
  string DetailTime(string value){DateTime time;return DateTime.TryParse(value,out time)?time.ToLocalTime().ToString("MM-dd HH:mm"):"";}
  string ModelLabel(Agent agent){var provider=state.providers.FirstOrDefault(v=>v.id==agent.providerId);return provider==null?Ui.T("未绑定模型"):provider.name+" · "+provider.model;}
  string RecipientName(Project p,string target){var person=p.team.FirstOrDefault(a=>a.id==target);return String.IsNullOrEmpty(target)?Ui.T("项目总体"):person==null?Ui.T("成员已移除"):person.name;}
  void ClearDetailDrafts(){foreach(var key in detailDrafts.Keys.Where(k=>k.StartsWith(detailProjectId+"/",StringComparison.Ordinal)).ToArray())detailDrafts.Remove(key);}
  bool HasDetailDraft(){return detailDrafts.Any(k=>k.Key.StartsWith(detailProjectId+"/",StringComparison.Ordinal)&&!String.IsNullOrEmpty(k.Value));}

  FlowLayoutPanel DetailFlow(Color color){return new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,BackColor=color,Padding=new Padding(Dpx(14))};}
  Label DetailLabel(string text,int height,bool bold){return new Label{Text=text,Height=Dpx(height),AutoSize=false,AutoEllipsis=true,ForeColor=ink,Font=bold?new Font(Font,FontStyle.Bold):Font,Margin=new Padding(0,0,0,Dpx(10))};}
  Button DetailButton(string text,Action action,int height){
    var button=Button(text,action,false);button.Height=Dpx(height);button.Tag=Dpx(height);button.Margin=new Padding(0,0,0,Dpx(10));button.TextAlign=ContentAlignment.MiddleLeft;button.Padding=new Padding(Dpx(12),Dpx(4),Dpx(8),Dpx(4));return button;
  }
  void FitDetailFlow(FlowLayoutPanel panel){
    if(panel==null||panel.IsDisposed)return;
    int width=Math.Max(80,panel.ClientSize.Width-panel.Padding.Horizontal-SystemInformation.VerticalScrollBarWidth-2);
    foreach(Control child in panel.Controls){child.Width=width;var button=child as Button;if(button!=null&&button.Tag is int){int measured=TextRenderer.MeasureText(button.Text,button.Font,new Size(Math.Max(40,width-button.Padding.Horizontal-16),0),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix).Height;button.Height=Math.Max((int)button.Tag,measured+button.Padding.Vertical+Dpx(12));}}
  }
  FlowLayoutPanel DetailDisclosure(string heading){
    var box=new FlowLayoutPanel{AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Visible=false,Margin=new Padding(0,0,0,Dpx(12))};
    var toggle=DetailButton("+  "+heading,()=>{},40);
    toggle.Click+=(s,e)=>{box.Visible=!box.Visible;toggle.Text=(box.Visible?"−  ":"+  ")+heading;};
    detailLeft.Controls.Add(toggle);detailLeft.Controls.Add(box);
    box.SizeChanged+=(s,e)=>{foreach(Control c in box.Controls)c.Width=Math.Max(80,box.ClientSize.Width-4);};
    return box;
  }
  void DetailReadOnly(FlowLayoutPanel panel,string text,int height){
    panel.Controls.Add(new TextBox{Text=(text??"").Replace("\r\n","\n").Replace("\n",Environment.NewLine),Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Height=Dpx(height),BorderStyle=BorderStyle.None,BackColor=panel.BackColor,Margin=new Padding(0,0,0,Dpx(10))});
  }
  void RenderDetails(Project p){
    if(detailWorkspace!=null){detailWorkspace.Dispose();detailWorkspace=null;}
    detailProjectId=p.id;
    string saved;detailTarget=detailTargets.TryGetValue(p.id,out saved)?saved:"";
    if(detailTarget!=""&&!p.team.Any(a=>a.id==detailTarget))detailTarget="";
    detailSnapshot=null;detailHistorySnapshot=null;detailPlanSnapshot=null;detailPeople.Clear();
    detailWorkspace=new Panel{Dock=DockStyle.Fill,BackColor=Color.FromArgb(241,245,249),Padding=new Padding(Dpx(8))};
    main.Controls.Add(detailWorkspace);detailWorkspace.BringToFront();
    detailColumns=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty,Padding=Padding.Empty};
    detailColumns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,22));
    detailColumns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,54));
    detailColumns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,24));
    detailColumns.RowStyles.Add(new RowStyle(SizeType.Percent,100));
    detailWorkspace.Controls.Add(detailColumns);

    detailLeft=DetailFlow(Color.FromArgb(245,246,248));detailLeft.Margin=new Padding(0,0,Dpx(8),0);detailColumns.Controls.Add(detailLeft,0,0);
    detailLeft.Controls.Add(DetailButton(Ui.T("← 返回工作空间"),()=>Navigate("overview"),40));
    detailLeft.Controls.Add(DetailLabel(p.name,48,true));
    var all=DetailButton("◉  "+Ui.T("项目总体")+"\n"+Ui.T("向整个团队下达命令"),()=>SelectDetailTarget(""),64);detailPeople[""]=all;detailLeft.Controls.Add(all);
    var goalPanel=DetailDisclosure(Ui.T("当前目标"));DetailReadOnly(goalPanel,p.goal+"\n\n"+p.constraints,120);
    goalPanel.Controls.Add(DetailButton(Ui.T("展开阅读"),()=>ReadDocument(Current.goal+"\n\n"+Current.constraints),36));
    detailPlanPanel=DetailDisclosure(Ui.T("当前计划"));
    RefreshDetailPlan(p);
    detailLeft.Controls.Add(DetailLabel(Ui.T("人员配备"),30,true));
    foreach(var group in p.team.GroupBy(DepartmentName)){
    detailLeft.Controls.Add(DetailLabel(group.Key,30,true));
    foreach(var agent in group.OrderBy(a=>a.level)){
      var captured=agent;
      var manager=p.team.FirstOrDefault(a=>a.id==agent.managerId);
      var card=DetailButton("L"+agent.level+"  "+agent.name+"\n"+ShortText(agent.role,32)+"\n"+ShortText(ModelLabel(agent),42)+"\n"+Ui.T("汇报给：")+(manager==null?Ui.T("项目决策者"):manager.name),()=>SelectDetailTarget(captured.id),116);
      card.AccessibleName=agent.name+" · "+agent.role+" · "+ModelLabel(agent);
      detailPeople[agent.id]=card;detailLeft.Controls.Add(card);
    }}
    detailLeft.Controls.Add(DetailButton(Ui.T("管理组织与模型"),()=>Navigate("team"),40));
    detailLeft.SizeChanged+=(s,e)=>FitDetailFlow(detailLeft);

    detailCenter=new Panel{Dock=DockStyle.Fill,BackColor=Color.White,Margin=new Padding(0,0,Dpx(8),0),Padding=new Padding(Dpx(16),Dpx(14),Dpx(20),Dpx(12))};detailColumns.Controls.Add(detailCenter,1,0);
    var header=new Panel{Dock=DockStyle.Top,Height=Dpx(100)};
    detailHeading=DetailLabel("",30,true);detailHeading.Dock=DockStyle.Top;detailHeading.Font=new Font(Font.FontFamily,15F,FontStyle.Bold);
    detailSubtitle=DetailLabel("",28,false);detailSubtitle.Dock=DockStyle.Top;detailSubtitle.ForeColor=Color.DimGray;
    detailProgress=DetailLabel("",30,false);detailProgress.Dock=DockStyle.Top;detailProgress.ForeColor=Color.DimGray;
    header.Controls.Add(detailProgress);header.Controls.Add(detailSubtitle);header.Controls.Add(detailHeading);
    var historyToggle=DetailButton(Ui.T("输出历史"),()=>{detailHistoryOverride=!(detailColumns.ColumnStyles[2].Width>0);LayoutDetailColumns();},34);historyToggle.Dock=DockStyle.Top;detailCenter.Controls.Add(historyToggle);

    var composer=new Panel{Dock=DockStyle.Bottom,Height=Dpx(174),BackColor=Color.FromArgb(248,250,252),Padding=new Padding(Dpx(12))};
    detailSendTo=DetailLabel("",27,false);detailSendTo.Dock=DockStyle.Top;
    detailInput=new TextBox{Multiline=true,MaxLength=8000,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.None,BackColor=composer.BackColor,AcceptsReturn=true,AccessibleName=Ui.T("消息或新指令")};
    var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=Dpx(48),AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.LeftToRight,WrapContents=true};
    detailSend=Button(Ui.T("发送  Ctrl+Enter"),async()=>await SendDetailMessage(),true);detailSend.Width=Dpx(160);detailSend.Height=Dpx(36);
    detailPause=Button(Ui.T("暂停"),async()=>await PauseDetailWork(),false);detailPause.Width=Dpx(82);detailPause.Height=Dpx(36);
    buttons.Controls.Add(detailSend);buttons.Controls.Add(detailPause);
    composer.Controls.Add(detailInput);composer.Controls.Add(buttons);composer.Controls.Add(detailSendTo);SoftUi.Round(composer,Dpx(16));
    detailMessages=DetailFlow(Color.White);detailMessages.Padding=new Padding(0,Dpx(12),0,Dpx(12));
    detailCenter.Controls.Add(detailMessages);detailCenter.Controls.Add(composer);detailCenter.Controls.Add(header);detailCenter.Controls.SetChildIndex(historyToggle,1);detailCenter.Controls.SetChildIndex(detailMessages,0);
    detailMessages.SizeChanged+=(s,e)=>LayoutDetailMessages();

    detailHistoryPane=new Panel{Dock=DockStyle.Fill,BackColor=Color.FromArgb(248,250,252),Margin=Padding.Empty,Padding=new Padding(Dpx(10),Dpx(16),Dpx(6),0)};detailColumns.Controls.Add(detailHistoryPane,2,0);
    var historyHeader=new Panel{Dock=DockStyle.Top,Height=Dpx(98)};
    var historyTitle=DetailLabel(Ui.T("模型输出历史"),30,true);historyTitle.Dock=DockStyle.Top;
    detailHistoryFilter=new ComboBox{Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDownList,AccessibleName=Ui.T("筛选输出成员")};
    detailHistoryFilter.Items.Add(new Choice("",Ui.T("全部成员")));foreach(var agent in p.team)detailHistoryFilter.Items.Add(new Choice(agent.id,agent.name));detailHistoryFilter.SelectedIndex=0;
    detailHistoryCount=DetailLabel("",28,false);detailHistoryCount.Dock=DockStyle.Bottom;detailHistoryCount.ForeColor=Color.DimGray;
    historyHeader.Controls.Add(detailHistoryFilter);historyHeader.Controls.Add(detailHistoryCount);historyHeader.Controls.Add(historyTitle);
    detailHistory=DetailFlow(detailHistoryPane.BackColor);detailHistory.Padding=new Padding(0,Dpx(10),0,0);
    detailHistoryPane.Controls.Add(detailHistory);detailHistoryPane.Controls.Add(historyHeader);
    detailHistory.SizeChanged+=(s,e)=>FitDetailFlow(detailHistory);
    detailHistoryFilter.SelectedIndexChanged+=(s,e)=>{detailHistorySnapshot=null;RefreshDetailsChat(Current);};

    detailInput.TextChanged+=(s,e)=>{detailSend.Enabled=!detailSending&&!String.IsNullOrWhiteSpace(detailInput.Text);if(detailUpdating)return;detailDrafts[DraftKey()]=detailInput.Text;dirty=HasDetailDraft();UpdateFooter();};
    detailInput.KeyDown+=(s,e)=>{if(e.Control&&e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;if(!detailSending)detailSend.PerformClick();}};
    SoftUi.Round(detailCenter,Dpx(18));SoftUi.Round(detailHistoryPane,Dpx(18));detailWorkspace.SizeChanged+=(s,e)=>LayoutDetailColumns();LayoutDetailColumns();
    SelectDetailTarget(detailTarget);
    detailSend.Enabled=!String.IsNullOrWhiteSpace(detailInput.Text);
    FitDetailFlow(detailLeft);
  }
  void ShowDetailTask(string id){
    var p=Current;var task=p.plan==null?null:p.plan.tasks.FirstOrDefault(t=>t.id==id);if(task==null)return;
    Result result;p.results.TryGetValue(id,out result);
    ReadDocument(task.title+"\n\n"+task.description+"\n\n"+Ui.T("验收：")+task.acceptance+"\n\n"+(result==null?Ui.T("等待执行"):result.text??result.error??result.status));
  }
  void SelectDetailTarget(string target){
    if(detailSending)return;
    var p=Current;if(p==null||p.id!=detailProjectId)return;
    if(target!=""&&!p.team.Any(a=>a.id==target))return;
    detailTarget=target;detailTargets[p.id]=target;
    detailUpdating=true;string draft;detailInput.Text=detailDrafts.TryGetValue(DraftKey(),out draft)?draft:"";detailUpdating=false;
    foreach(var pair in detailPeople){pair.Value.BackColor=pair.Key==target?Color.FromArgb(224,231,255):Color.White;pair.Value.FlatAppearance.BorderColor=pair.Key==target?green:Color.FromArgb(226,232,240);}
    detailHeading.Text=RecipientName(p,target);
    var member=p.team.FirstOrDefault(a=>a.id==target);
    detailSubtitle.Text=member==null?Ui.T("团队对话 · 每位成员分别回复"):ShortText(member.role+" · "+ModelLabel(member),100);
    detailSendTo.ForeColor=green;detailSendTo.Text="↗ "+Ui.T("发送给")+"： "+RecipientName(p,target);
    detailSnapshot=null;RefreshDetailsChat(p);dirty=HasDetailDraft();UpdateFooter();
  }
  async Task SendDetailMessage(){
    if(detailSending||busy)return;
    string message=detailInput.Text.Trim();if(message.Length==0)return;
    string projectId=detailProjectId,target=detailTarget,key=DraftKey();
    detailSending=true;busy=true;detailSend.Enabled=false;detailSend.Text=Ui.T("正在发送…");detailLeft.Enabled=false;detailInput.ReadOnly=true;
    try{
      await Post("projects/command",new{projectId,targetId=target,text=message});
      detailDrafts.Remove(key);
      detailUpdating=true;detailInput.Clear();detailUpdating=false;
      await RefreshState();RefreshDetailsChat(Current);
    }catch(Exception ex){MessageBox.Show(this,ex.Message,Ui.T("操作未完成"),MessageBoxButtons.OK,MessageBoxIcon.Warning);}
    finally{detailSending=false;busy=false;if(!detailInput.IsDisposed){detailSend.Text=Ui.T("发送  Ctrl+Enter");detailSend.Enabled=!String.IsNullOrWhiteSpace(detailInput.Text);detailLeft.Enabled=true;detailInput.ReadOnly=false;detailInput.Focus();}dirty=HasDetailDraft();UpdateFooter();}
  }
  async Task PauseDetailWork(){
    if(detailSending||busy)return;busy=true;
    try{await Post("projects/pause",new{projectId=detailProjectId});await RefreshState();RefreshDetailsChat(Current);}
    catch(Exception ex){MessageBox.Show(this,ex.Message,Ui.T("操作未完成"));}
    finally{busy=false;}
  }
  IEnumerable<ProjectCommand> DetailCommands(Project p){
    return (p.commands??new List<ProjectCommand>()).Where(c=>(c.targetId??"")==detailTarget);
  }
  void RefreshDetailsChat(Project p){
    if(p==null||detailWorkspace==null||detailWorkspace.IsDisposed||p.id!=detailProjectId)return;
    detailProgress.Text=p.results.Values.Count(r=>r.status=="done")+" / "+(p.plan==null?0:p.plan.tasks.Count)+Ui.T(" 项任务已提交")+"  ·  "+(p.mode=="demo"?Ui.T("演示模式"):Ui.T("真实 API 模式"));
    detailProgress.ForeColor=Active?Color.FromArgb(180,100,20):Color.FromArgb(15,118,110);
    detailProgress.Text=(Active?"●  ":"○  ")+detailProgress.Text;
    detailPause.Enabled=Active&&!detailSending;
    RefreshDetailPlan(p);
    var commands=DetailCommands(p).ToList();string snapshot=json.Serialize(commands);
    if(snapshot!=detailSnapshot){
      bool atBottom=detailMessages.VerticalScroll.Value+detailMessages.ClientSize.Height>=detailMessages.DisplayRectangle.Height-Dpx(28);
      var scroll=detailMessages.AutoScrollPosition;bool first=detailSnapshot==null;detailSnapshot=snapshot;
      detailMessages.SuspendLayout();foreach(Control control in detailMessages.Controls.Cast<Control>().ToArray())control.Dispose();detailMessages.Controls.Clear();
      if(commands.Count==0)AddDetailBubble(Ui.T("开始对话"),Ui.T("从左侧选择项目总体或某位成员，在下方输入新指令。这里会保留当前对象的对话。"),false);
      foreach(var item in commands){
        AddDetailBubble(Ui.T("你")+" · "+DetailTime(item.at),item.text,true);
        foreach(var reply in item.replies??new List<CommandReply>())AddDetailBubble(reply.agentName,reply.text,false);
        if(item.status!="done")AddDetailBubble(CommandStatus(item.status),item.error??(item.status=="queued"?Ui.T("等待当前运行完成后回复。"):Ui.T("模型正在处理，请稍候…")),false);
      }
      LayoutDetailMessages();detailMessages.ResumeLayout();
      if(first||atBottom){if(detailMessages.Controls.Count>0)detailMessages.ScrollControlIntoView(detailMessages.Controls[detailMessages.Controls.Count-1]);}
      else detailMessages.AutoScrollPosition=new Point(-scroll.X,-scroll.Y);
    }
    RefreshDetailHistory(p);
  }
  void AddDetailBubble(string sender,string text,bool user){
    var row=new Panel{Height=Dpx(100),Margin=new Padding(0,0,0,Dpx(16)),Tag=user};
    var bubble=new Panel{BackColor=user?Color.FromArgb(49,46,129):Color.FromArgb(243,244,246),Padding=new Padding(Dpx(14)),Tag=text??""};
    if(sender==CommandStatus("failed"))bubble.BackColor=Color.FromArgb(255,237,230);
    else if(sender==CommandStatus("queued")||sender==CommandStatus("running"))bubble.BackColor=Color.FromArgb(255,247,221);
    var label=new Label{Text=sender,ForeColor=user?Color.FromArgb(199,210,254):Color.FromArgb(99,102,112),Height=Dpx(23),Dock=DockStyle.Top};
    var body=new Label{Text=(text??"").Length>1000?(text.Substring(0,1000)+"…"):text,ForeColor=user?Color.White:ink,AutoSize=false,Dock=DockStyle.Fill,UseMnemonic=false};
    bubble.Controls.Add(body);bubble.Controls.Add(label);
    var read=DetailButton(Ui.T("展开阅读"),()=>ReadDocument(text),28);read.Dock=DockStyle.Bottom;read.FlatAppearance.BorderSize=0;read.BackColor=bubble.BackColor;read.ForeColor=user?Color.White:green;read.Height=Dpx(28);read.Padding=Padding.Empty;
    read.FlatAppearance.MouseOverBackColor=user?Color.FromArgb(67,56,155):Color.FromArgb(224,231,255);
    bubble.Controls.Add(read);SoftUi.Round(bubble,Dpx(16));row.Controls.Add(bubble);detailMessages.Controls.Add(row);
  }
  void LayoutDetailMessages(){
    if(detailMessages==null||detailMessages.IsDisposed)return;
    int width=Math.Max(180,detailMessages.ClientSize.Width-SystemInformation.VerticalScrollBarWidth-6);
    foreach(Control row in detailMessages.Controls){
      row.Width=width;var bubble=row.Controls[0];int bubbleWidth=Math.Max(160,(int)(width*.90));
      string text=Convert.ToString(bubble.Tag);string preview=text.Length>1000?text.Substring(0,1000)+"…":text;
      int height=TextRenderer.MeasureText(preview,Font,new Size(Math.Max(80,bubbleWidth-Dpx(28)),0),TextFormatFlags.WordBreak|TextFormatFlags.TextBoxControl).Height;
      height=Math.Min(Dpx(330),Math.Max(Dpx(28),height));
      row.Height=height+Dpx(86);bubble.Bounds=new Rectangle((bool)row.Tag?width-bubbleWidth:0,0,bubbleWidth,row.Height);
    }
  }
  class DetailOutput {public string Member,Name,Kind,Text,Time;}
  void RefreshDetailPlan(Project p){
    string snapshot=json.Serialize(new{p.plan,status=p.results.ToDictionary(x=>x.Key,x=>x.Value.status)});
    if(snapshot==detailPlanSnapshot)return;detailPlanSnapshot=snapshot;
    detailPlanPanel.SuspendLayout();foreach(Control c in detailPlanPanel.Controls.Cast<Control>().ToArray())c.Dispose();detailPlanPanel.Controls.Clear();
    DetailReadOnly(detailPlanPanel,p.plan==null?Ui.T("暂无计划"):p.plan.summary,100);
    if(p.plan!=null)foreach(var task in p.plan.tasks){var captured=task;Result result;p.results.TryGetValue(task.id,out result);string status=result==null?Ui.T("等待执行"):result.status=="done"?Ui.T("已提交"):result.status=="running"?Ui.T("执行中"):Ui.T("需重试");detailPlanPanel.Controls.Add(DetailButton(task.id+" · "+ShortText(task.title,26)+"\n"+status,()=>ShowDetailTask(captured.id),64));}
    detailPlanPanel.Controls.Add(DetailButton(Ui.T("打开计划与审批"),()=>Navigate("planning"),40));
    foreach(Control c in detailPlanPanel.Controls)c.Width=Math.Max(80,detailPlanPanel.ClientSize.Width-4);
    detailPlanPanel.ResumeLayout();
  }
  List<DetailOutput> CollectDetailHistory(Project p){
    var entries=new List<DetailOutput>();
    foreach(var item in p.discussion??new List<Discussion>())entries.Add(new DetailOutput{Member=item.agentId,Name=item.agentName,Kind=Ui.T("计划讨论")+" · "+item.round,Text=item.text,Time=item.at});
    if(p.plan!=null)foreach(var task in p.plan.tasks){Result result;if(p.results.TryGetValue(task.id,out result)&&!String.IsNullOrEmpty(result.text)){var agent=p.team.FirstOrDefault(a=>a.id==task.agentId);entries.Add(new DetailOutput{Member=task.agentId,Name=agent==null?task.agentId:agent.name,Kind=task.id+" · "+task.title,Text=result.text,Time=result.finishedAt??result.startedAt});}}
    foreach(var command in p.commands??new List<ProjectCommand>())foreach(var reply in command.replies??new List<CommandReply>())entries.Add(new DetailOutput{Member=reply.agentId,Name=reply.agentName,Kind=Ui.T("指令回复"),Text=reply.text,Time=command.at});
    if(!String.IsNullOrEmpty(p.review)){var reviewer=p.team.LastOrDefault();entries.Add(new DetailOutput{Member=reviewer==null?"":reviewer.id,Name=reviewer==null?"":reviewer.name,Kind=Ui.T("最终审核意见"),Text=p.review,Time=p.updatedAt});}
    return entries.OrderByDescending(e=>e.Time??"").ToList();
  }
  void RefreshDetailHistory(Project p){
    string filter=ChoiceValue(detailHistoryFilter);var entries=CollectDetailHistory(p).Where(e=>filter==""||e.Member==filter).ToList();string snapshot=json.Serialize(entries);
    if(snapshot==detailHistorySnapshot)return;detailHistorySnapshot=snapshot;
    var scroll=detailHistory.AutoScrollPosition;detailHistory.SuspendLayout();foreach(Control control in detailHistory.Controls.Cast<Control>().ToArray())control.Dispose();detailHistory.Controls.Clear();
    detailHistoryCount.Text=entries.Count+" "+Ui.T("条记录 · 点击阅读全文");
    if(entries.Count==0)detailHistory.Controls.Add(DetailLabel(Ui.T("暂无输出。模型回复、计划讨论和任务成果会显示在这里。"),100,false));
    foreach(var entry in entries){var captured=entry;detailHistory.Controls.Add(DetailButton(entry.Name+" · "+DetailTime(entry.Time)+"\n"+entry.Kind+"\n"+ShortText(entry.Text,70),()=>ReadDocument(captured.Name+"\n"+captured.Kind+"\n\n"+captured.Text),112));}
    FitDetailFlow(detailHistory);detailHistory.ResumeLayout();detailHistory.AutoScrollPosition=new Point(-scroll.X,-scroll.Y);
  }
  void TestDetailsWorkspace(Project p){
    var oldSize=ClientSize;
    ClientSize=new Size(Dpx(1000),Dpx(700));detailHistoryOverride=null;PerformLayout();LayoutDetailColumns();
    if(detailColumns.ColumnStyles[2].Width!=0)throw new Exception("History must collapse in narrow windows");
    detailHistoryOverride=true;LayoutDetailColumns();if(detailColumns.ColumnStyles[2].Width<=0)throw new Exception("History toggle did not open");
    detailHistoryOverride=null;ClientSize=oldSize;PerformLayout();LayoutDetailColumns();
    if(detailWorkspace==null||detailWorkspace.IsDisposed||detailPeople.Count!=p.team.Count+1)throw new Exception("Missing detail workspace or recipient cards");
    if(!DetailCommands(p).SelectMany(c=>c.replies).Any(r=>r.text=="Reply visible"))throw new Exception("Conversation reply missing");
    detailInput.Text="Team draft";SelectDetailTarget("b");detailInput.Text="Member draft";
    RefreshDetailsChat(p);if(detailInput.Text!="Member draft"||detailTarget!="b")throw new Exception("Polling changed recipient or draft");
    SelectDetailTarget("");if(detailInput.Text!="Team draft")throw new Exception("Team draft lost");
    SelectDetailTarget("b");if(detailInput.Text!="Member draft"||DetailCommands(p).Any())throw new Exception("Private conversation mixed with team conversation");
    if(!CollectDetailHistory(p).Any(e=>e.Text=="Reply visible"))throw new Exception("Output history missing reply");
    ClearDetailDrafts();detailUpdating=true;detailInput.Clear();detailUpdating=false;dirty=false;
  }
}
}

