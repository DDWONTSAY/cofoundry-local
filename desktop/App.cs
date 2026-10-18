using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Cofoundry {
public class Agent { public string id,name,role,providerId,managerId,department; public int level; }
public class WorkTask { public string id,title,description,agentId,acceptance; public List<string> dependsOn; }
public class Plan { public string summary,risks; public List<WorkTask> tasks; }
public class Approval { public string hash,at; public int revision; }
public class Discussion { public string agentId,agentName,role,text,at; public int round; }
public class Result { public string status,text,error,startedAt,finishedAt; }
public class Event { public string at,text; }
public class Provider { public string id,name,model,endpoint,protocol,bodyTemplate,headerTemplate,responsePath; public bool hasKey; }
public class CommandReply { public string agentId,agentName,text; }
public class ProjectCommand { public string id,text,targetId,status,at,error; public List<CommandReply> replies; }
public class Project {
  public string locale; public List<ProjectCommand> commands;
  public string id,name,goal,constraints,mode,status,error,review,updatedAt;
  public int levels,concurrency,maxCalls,calls,revision;
  public List<Agent> team; public Plan plan; public Approval approval;
  public List<Discussion> discussion; public Dictionary<string,Result> results; public List<Event> events;
}
public class State { public List<Project> projects; public List<Provider> providers; public List<string> active; }
public class Choice { public string Value {get;set;} public string Label {get;set;} public Choice(string v,string l){Value=v;Label=l;} public override string ToString(){return Label;} }

static class Program {
  [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetProcessDPIAware();
  [STAThread] static void Main(string[] args) {
    SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
    if(args.Contains("--self-test")) {try {MainForm.SelfTest();File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"desktop-self-test.txt"),"PASS: native pages, organization chart, conversation, edit preservation, bindings and JSON round-trip",Encoding.UTF8);} catch(Exception e){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"desktop-self-test.txt"),e.ToString());Environment.ExitCode=1;}return;}
    bool created; using(var mutex=new Mutex(true,"Local\\CofoundryDesktop_4317",out created)) {
      if(!created){MessageBox.Show(Ui.T("共创团队已经运行，请从任务栏打开已有窗口。"),Ui.T("共创团队"));return;}
      Application.Run(new MainForm());
    }
  }
}

public partial class MainForm : Form {
  readonly string root=AppDomain.CurrentDomain.BaseDirectory;
  readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=16000000};
  readonly HttpClient http=new HttpClient(new HttpClientHandler{UseProxy=false}){BaseAddress=new Uri("http://127.0.0.1:4317/"),Timeout=TimeSpan.FromSeconds(130)};
  readonly Color green=Color.FromArgb(79,70,229),ink=Color.FromArgb(24,24,27),paper=Color.FromArgb(244,247,250);
  readonly Dictionary<string,Button> navigation=new Dictionary<string,Button>();
  readonly Dictionary<string,bool> expandedSections=new Dictionary<string,bool>();
  Func<Task> saveCurrent;
  float uiScale=1F; bool scaling; bool editingBrief;
  State state=new State{projects=new List<Project>(),providers=new List<Provider>(),active=new List<string>()};
  string selected,section="overview"; bool busy,dirty,rendering,loading=true;
  Process ownedServer; Panel main,sidebar; FlowLayoutPanel content; Label footer,title; ComboBox projectPicker;
  System.Windows.Forms.Timer timer; TextBox projectName,goal,constraints,summary,risks; ComboBox mode;
  NumericUpDown levels,concurrency,maxCalls; DataGridView teamGrid,taskGrid;
  static readonly Dictionary<string,string> statusNames=new Dictionary<string,string>{{"draft",Ui.T("待制定计划")},{"discussing",Ui.T("团队讨论中")},{"awaiting_approval",Ui.T("等待用户批准")},{"approved",Ui.T("计划已批准")},{"running",Ui.T("执行中")},{"paused",Ui.T("已暂停")},{"delivered",Ui.T("等待用户验收")},{"completed",Ui.T("项目已完成")}};
  Project Current {get{return state.projects.FirstOrDefault(x=>x.id==selected);}}
  bool Active {get{return selected!=null&&state.active.Contains(selected);}}

  public MainForm() {
    SuspendLayout();
    using(var display=Graphics.FromHwnd(IntPtr.Zero))uiScale=Math.Max(1F,display.DpiX/96F);
    Text=Ui.T("共创团队 · 桌面版"); StartPosition=FormStartPosition.CenterScreen; MinimumSize=new Size(1000,680); Size=new Size(1240,820); BackColor=paper; Font=new Font("Microsoft YaHei UI",10F); AutoScaleMode=AutoScaleMode.None;
    http.DefaultRequestHeaders.Add("X-Cofoundry","local");
    sidebar=new Panel{Dock=DockStyle.Left,Width=232,BackColor=Color.FromArgb(241,245,249),Padding=new Padding(18,24,18,16)};
    var brand=new Label{Text=Ui.T("共创团队"),ForeColor=ink,Font=new Font(Font.FontFamily,17,FontStyle.Bold),Dock=DockStyle.Top,Height=58}; sidebar.Controls.Add(brand);
    var nav=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(0,12,0,0)};
    var local=new Label{Text=Ui.T("工作空间"),ForeColor=Color.FromArgb(100,116,139),Height=28,Width=184,Margin=new Padding(0)};nav.Controls.Add(local);
    projectPicker=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=192,Height=36,Margin=new Padding(0,6,0,12)};
    projectPicker.SelectedIndexChanged+=(s,e)=>{if(rendering)return;var c=projectPicker.SelectedItem as Choice;if(c==null||c.Value==selected)return;if(!LeaveEdits()){UpdatePicker();return;}selected=c.Value;Render();};nav.Controls.Add(projectPicker);
    var newButton=Button(Ui.T("＋ 新建项目"),async()=>await Run(NewProject),false);newButton.Width=192;newButton.Margin=new Padding(0,8,0,0);nav.Controls.Add(newButton);
    foreach(var pair in new[]{new[]{"overview",Ui.T("项目总览")},new[]{"details",Ui.T("项目详情与指令")},new[]{"planning",Ui.T("计划讨论与审批")},new[]{"team",Ui.T("团队组织")},new[]{"results",Ui.T("执行与成果")},new[]{"providers",Ui.T("API 连接")}}) {
      var key=pair[0];var b=Button(pair[1],()=>{if(LeaveEdits()){section=key;editingBrief=false;Render();}},false);b.Width=192;b.Height=44;b.TextAlign=ContentAlignment.MiddleLeft;b.Padding=new Padding(12,0,0,0);b.Margin=new Padding(0,8,0,0);navigation[key]=b;nav.Controls.Add(b);
    }
    var info=new Label{Text=Ui.T("本机保存\n通过 API 连接你的模型"),ForeColor=Color.FromArgb(100,116,139),Width=192,Height=65,Margin=new Padding(0,32,0,0)};nav.Controls.Add(info);
    sidebar.Controls.Add(nav);nav.BringToFront();
    footer=new Label{Dock=DockStyle.Bottom,Height=34,Text=Ui.T("正在连接本机工作空间…"),Padding=new Padding(20,8,0,0),ForeColor=Color.DimGray,BackColor=Color.White};
    main=new Panel{Dock=DockStyle.Fill,Padding=new Padding(28,22,24,10)};
    title=new Label{Dock=DockStyle.Top,Height=64,Font=new Font(Font.FontFamily,20,FontStyle.Bold),ForeColor=ink,Text=Ui.T("共创团队")};
    content=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,WrapContents=false,FlowDirection=FlowDirection.TopDown};
    content.SizeChanged+=(s,e)=>SizeCards();main.Controls.Add(content);main.Controls.Add(title);
    Controls.Add(main);Controls.Add(sidebar);Controls.Add(footer);
    scaling=true;Scale(new SizeF(uiScale,uiScale));scaling=false;ResumeLayout(true);
    var available=Screen.FromControl(this).WorkingArea;Size=new Size(Math.Min(Width,available.Width-24),Math.Min(Height,available.Height-24));
    Shown+=async(s,e)=>{await Run(async()=>{await EnsureServer();await RefreshState();loading=false;Render();});};
    timer=new System.Windows.Forms.Timer{Interval=1600};timer.Tick+=async(s,e)=>{if(busy||loading||IsDisposed)return;busy=true;try{bool was=Active;string before=json.Serialize(Current);await RefreshState();UpdateFooter();if(section=="details"&&Current!=null)RefreshDetailsChat(Current);else if(!dirty&&(was||Active)&&before!=json.Serialize(Current))Render(true);}catch(Exception ex){footer.Text=Ui.T("本机服务连接失败：")+ex.Message;}finally{busy=false;}};timer.Start();
    FormClosing+=(s,e)=>{if(busy){e.Cancel=true;MessageBox.Show(Ui.T("正在处理操作，请稍后关闭。"),Ui.T("共创团队"));return;}if(!LeaveEdits()){e.Cancel=true;return;}if(ownedServer!=null&&state.active.Count>0){e.Cancel=true;MessageBox.Show(Ui.T("还有项目在运行，请先暂停运行中的项目再退出。"),Ui.T("共创团队"));return;}timer.Stop();if(ownedServer!=null&&!ownedServer.HasExited)ownedServer.Kill();http.Dispose();};
  }
  async Task EnsureServer() {
    try {using(var cancel=new CancellationTokenSource(1800)){var r=await http.GetAsync("api/state",cancel.Token);var text=await r.Content.ReadAsStringAsync();var found=json.Deserialize<State>(text);if(r.IsSuccessStatusCode&&found.projects!=null&&found.providers!=null)return;throw new Exception(Ui.T("4317 端口已被其他服务占用"));}}
    catch(OperationCanceledException){}catch(HttpRequestException){}
    string node=Path.Combine(root,"runtime","node.exe");
    if(!File.Exists(node))throw new Exception(Ui.T("缺少 runtime\\node.exe，请保留完整软件目录。"));
    var start=new ProcessStartInfo(node,"\""+Path.Combine(root,"server.mjs")+"\""){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
    ownedServer=Process.Start(start);ownedServer.BeginOutputReadLine();ownedServer.BeginErrorReadLine();
    for(int i=0;i<35;i++){await Task.Delay(200);if(ownedServer.HasExited)throw new Exception(Ui.T("本机服务启动失败，请检查端口占用和目录写入权限。"));try{await RefreshState();return;}catch(HttpRequestException){}}
    throw new Exception(Ui.T("本机服务启动超时"));
  }
  async Task RefreshState(){var r=await http.GetAsync("api/state");r.EnsureSuccessStatusCode();state=json.Deserialize<State>(await r.Content.ReadAsStringAsync());if(state.projects==null||state.providers==null||state.active==null)throw new Exception(Ui.T("本机服务响应格式不正确"));if(Current==null)selected=state.projects.Select(x=>x.id).FirstOrDefault();}
  async Task<Dictionary<string,object>> Post(string route,object body){var response=await http.PostAsync("api/"+route,new StringContent(json.Serialize(body),Encoding.UTF8,"application/json"));var data=json.Deserialize<Dictionary<string,object>>(await response.Content.ReadAsStringAsync());if(!response.IsSuccessStatusCode)throw new Exception(data.ContainsKey("error")?Ui.T(Convert.ToString(data["error"])):Ui.T("请求失败"));return data;}
  async Task Run(Func<Task> action){if(busy)return;busy=true;main.Enabled=false;sidebar.Enabled=false;try{await action();if(!dirty){await RefreshState();Render(true);}}catch(Exception e){MessageBox.Show(this,e.Message,Ui.T("操作未完成"),MessageBoxButtons.OK,MessageBoxIcon.Warning);}finally{busy=false;main.Enabled=true;sidebar.Enabled=true;UpdateFooter();}}
  bool LeaveEdits(){if(!dirty)return true;if(MessageBox.Show(this,Ui.T("有未保存的修改。放弃修改并继续？"),Ui.T("未保存的修改"),MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return false;if(section=="details")ClearDetailDrafts();dirty=false;return true;}
  void Changed(object s,EventArgs e){var control=s as Control;if(!rendering&&control!=null&&control.FindForm()==this){dirty=true;UpdateFooter();}}
  protected override bool ProcessCmdKey(ref Message msg,Keys keyData){if(keyData==(Keys.Control|Keys.S)&&saveCurrent!=null&&!busy&&!Active){SaveShortcut();return true;}return base.ProcessCmdKey(ref msg,keyData);}
  async void SaveShortcut(){if(saveCurrent!=null)await Run(saveCurrent);}
  void UpdateFooter(){var p=Current;footer.Text=loading?Ui.T("正在启动本机服务…"):p==null?Ui.T("本机就绪 · 创建项目或配置 API 开始使用"):(statusNames.ContainsKey(p.status)?statusNames[p.status]:p.status)+"  |  "+(p.mode=="demo"?Ui.T("演示模式（不调用 API）"):Ui.T("真实 API 模式"))+Ui.T("  |  调用 ")+p.calls+" / "+p.maxCalls+(dirty?Ui.T("  |  有未保存的修改"):"");}
  void UpdatePicker(){rendering=true;projectPicker.Items.Clear();foreach(var p in state.projects)projectPicker.Items.Add(new Choice(p.id,p.name));for(int i=0;i<projectPicker.Items.Count;i++)if(((Choice)projectPicker.Items[i]).Value==selected)projectPicker.SelectedIndex=i;rendering=false;}
  void Render(bool preservePosition=false){var scroll=content.AutoScrollPosition;saveCurrent=null;rendering=true;content.SuspendLayout();foreach(Control c in content.Controls.Cast<Control>().ToArray())c.Dispose();content.Controls.Clear();UpdatePicker();rendering=true;var p=Current;title.Text=section=="providers"?Ui.T("API 模型连接"):p==null?Ui.T("让 AI 组成你的项目团队"):p.name;
    if(section=="details"&&p!=null){content.ResumeLayout();sidebar.Visible=false;title.Visible=false;content.Visible=false;main.Padding=Padding.Empty;RenderDetails(p);rendering=false;dirty=HasDetailDraft();UpdateFooter();return;}
    if(detailWorkspace!=null){detailWorkspace.Dispose();detailWorkspace=null;}sidebar.Visible=true;title.Visible=true;content.Visible=true;main.Padding=new Padding(Dpx(28),Dpx(22),Dpx(24),Dpx(10));
    if(section=="providers")RenderProviders();else if(p==null)RenderWelcome();else {AddNote((statusNames.ContainsKey(p.status)?statusNames[p.status]:p.status)+"  ·  "+(p.mode=="demo"?Ui.T("离线演示，无 API 消耗"):Ui.T("真实 API 模式"))+"  ·  "+p.team.Count+Ui.T(" 位成员 / ")+p.levels+Ui.T(" 层组织"));if(!String.IsNullOrEmpty(p.error))AddNote(p.error);if(section=="overview")RenderOverview(p);if(section=="planning")RenderPlanning(p);if(section=="team")RenderTeam(p);if(section=="details")RenderDetails(p);if(section=="results")RenderResults(p);}
    scaling=true;foreach(Control card in content.Controls)card.Scale(new SizeF(uiScale,uiScale));scaling=false;
    SizeCards();content.ResumeLayout();rendering=false;dirty=false;
    foreach(var item in navigation){bool chosen=item.Key==section;item.Value.BackColor=chosen?Color.FromArgb(224,231,255):sidebar.BackColor;item.Value.ForeColor=chosen?green:Color.FromArgb(71,85,105);item.Value.FlatAppearance.BorderSize=0;}
    content.AutoScrollPosition=preservePosition?new Point(-scroll.X,-scroll.Y):Point.Empty;
    UpdateFooter();}
  void SizeCards(){if(scaling)return;foreach(Control c in content.Controls){c.Width=Math.Max((int)(280*uiScale),Math.Min((int)(1120*uiScale),content.ClientSize.Width-(int)(24*uiScale)));FitCard(c);}}
  void FitCard(Control c){
    int width=Math.Max(Dpx(120),c.ClientSize.Width-c.Padding.Horizontal-Dpx(8));
    foreach(Control child in c.Controls){
      var label=child as Label;
      if(label!=null){label.MaximumSize=new Size(width,0);if(!label.AutoSize){label.Width=width;label.Height=Math.Max(label.Height,TextRenderer.MeasureText(label.Text,label.Font,new Size(width,0),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix).Height+Dpx(4));}continue;}
      var flow=child as FlowLayoutPanel;
      if(flow!=null){flow.MaximumSize=new Size(width,0);flow.MinimumSize=new Size(width,0);flow.Width=width;if(flow.FlowDirection==FlowDirection.TopDown)FitCard(flow);}
      else child.Width=width;
    }
  }
  FlowLayoutPanel Card(string heading){var c=new FlowLayoutPanel{FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,BackColor=Color.White,Padding=new Padding(24),Margin=new Padding(0,0,0,18),Width=850};if(!String.IsNullOrEmpty(heading))c.Controls.Add(new Label{Text=heading,AutoSize=true,Font=new Font(Font.FontFamily,12,FontStyle.Bold),ForeColor=ink,Margin=new Padding(0,0,0,16)});content.Controls.Add(c);SoftUi.Round(c,Dpx(16));c.SizeChanged+=(s,e)=>{if(!scaling)FitCard(c);};return c;}
  void AddNote(string text){var c=Card(null);c.BackColor=Color.FromArgb(238,242,255);c.Padding=new Padding(18,12,18,12);c.Controls.Add(new Label{Text=text,Height=30,AutoSize=false,ForeColor=Color.FromArgb(67,56,202),Width=750});}
  Label Note(FlowLayoutPanel panel,string text,int height){var l=new Label{Text=text,Height=height,Width=760,ForeColor=Color.DimGray,Margin=new Padding(0,0,0,12)};panel.Controls.Add(l);return l;}
  TextBox Field(FlowLayoutPanel panel,string label,string value,int height){panel.Controls.Add(new Label{Text=label,AutoSize=true,Margin=new Padding(0,8,0,6)});var t=new TextBox{Text=value??"",Width=760,Multiline=height>35,Height=height,ScrollBars=height>35?ScrollBars.Vertical:ScrollBars.None,BorderStyle=BorderStyle.FixedSingle,Margin=new Padding(0,0,0,8)};t.TextChanged+=Changed;panel.Controls.Add(t);return t;}
  ComboBox Select(FlowLayoutPanel panel,string label,List<Choice> choices,string value){panel.Controls.Add(new Label{Text=label,AutoSize=true,Margin=new Padding(0,8,0,6)});var c=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=760,Height=34};foreach(var x in choices)c.Items.Add(x);for(int i=0;i<c.Items.Count;i++)if(((Choice)c.Items[i]).Value==value)c.SelectedIndex=i;if(c.SelectedIndex<0&&c.Items.Count>0)c.SelectedIndex=0;c.SelectedIndexChanged+=Changed;panel.Controls.Add(c);return c;}
  NumericUpDown Number(FlowLayoutPanel panel,string label,int value,int min,int max){panel.Controls.Add(new Label{Text=label,AutoSize=true,Margin=new Padding(0,8,0,6)});var n=new NumericUpDown{Minimum=min,Maximum=max,Value=Math.Max(min,Math.Min(max,value)),Width=760};n.ValueChanged+=Changed;panel.Controls.Add(n);return n;}
  Button Button(string label,Action action,bool primary){var b=new SoftButton{Text=label,AutoSize=false,Width=164,Height=40,FlatStyle=FlatStyle.Flat,BackColor=primary?green:Color.White,ForeColor=primary?Color.White:ink,Margin=new Padding(0,8,12,0),Cursor=Cursors.Hand};b.FlatAppearance.BorderColor=primary?green:Color.FromArgb(203,213,225);b.FlatAppearance.MouseOverBackColor=primary?Color.FromArgb(67,56,202):Color.FromArgb(241,245,249);b.Click+=(s,e)=>action();return b;}
  FlowLayoutPanel Actions(FlowLayoutPanel card){var a=new FlowLayoutPanel{FlowDirection=FlowDirection.LeftToRight,WrapContents=true,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Width=760,Margin=new Padding(0,5,0,0)};card.Controls.Add(a);return a;}
  void Output(FlowLayoutPanel card,string text,int height,bool readingTools=false){var t=new TextBox{Text=(text??"").Replace("\r\n","\n").Replace("\n",Environment.NewLine),Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Height=height,Width=760,BackColor=Color.FromArgb(248,250,252),ForeColor=Color.FromArgb(51,65,85),BorderStyle=BorderStyle.FixedSingle,Margin=new Padding(0,8,0,12)};card.Controls.Add(t);if(readingTools){var tools=Actions(card);tools.Controls.Add(Button(Ui.T("展开阅读"),()=>ReadDocument(text),false));tools.Controls.Add(Button(Ui.T("复制文本"),()=>CopyText(text),false));}}
  void CopyText(string text){try{if(!String.IsNullOrEmpty(text)){Clipboard.SetText(text);footer.Text=Ui.T("文本已复制");}}catch(Exception){MessageBox.Show(this,Ui.T("剪贴板暂时不可用，请稍后再试。"),Ui.T("复制文本"));}}
  void ReadDocument(string text){using(var reader=Dialog(Ui.T("阅读内容"))){var output=new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,WordWrap=true,ScrollBars=ScrollBars.Vertical,Text=(text??"").Replace("\r\n","\n").Replace("\n",Environment.NewLine),Font=new Font(Font.FontFamily,12F),BackColor=Color.White,ForeColor=ink,BorderStyle=BorderStyle.None};reader.Padding=new Padding(24);reader.MaximizeBox=true;reader.Controls.Add(output);reader.ShowDialog(this);}}
  FlowLayoutPanel Disclosure(FlowLayoutPanel card,string heading,bool expanded){var key=(selected??"")+"/"+section+"/"+heading;bool remembered;if(expandedSections.TryGetValue(key,out remembered))expanded=remembered;var inner=new FlowLayoutPanel{FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoSize=true,Width=760,Visible=expanded,Margin=new Padding(0,8,0,8)};var toggle=Button((expanded?"−  ":"＋  ")+heading,()=>{},false);toggle.Width=760;toggle.TextAlign=ContentAlignment.MiddleLeft;toggle.FlatAppearance.BorderSize=0;toggle.BackColor=Color.FromArgb(248,250,252);toggle.Click+=(s,e)=>{expanded=!expanded;expandedSections[key]=expanded;inner.Visible=expanded;toggle.Text=(expanded?"−  ":"＋  ")+heading;};card.Controls.Add(toggle);card.Controls.Add(inner);inner.SizeChanged+=(s,e)=>{if(!scaling)FitCard(inner);};return inner;}
  void Navigate(string destination){if(!LeaveEdits())return;section=destination;editingBrief=false;Render();}
  void NextStep(Project p){var c=Card(Ui.T("下一步"));string text;string label;string destination;
    if(p.status=="completed"){text=Ui.T("项目已验收。可以查看成果，或导出一份留存。");label=Ui.T("查看项目成果");destination="results";}
    else if(p.status=="delivered"){text=Ui.T("团队已提交成果与审核意见，等待你验收。");label=Ui.T("审阅与验收");destination="results";}
    else if(p.status=="approved"||p.status=="running"||p.status=="paused"){text=Ui.T("计划已批准。前往执行页查看进度、启动或继续任务。");label=Ui.T("前往执行");destination="results";}
    else if(p.plan!=null){text=Ui.T("计划已准备好。请检查任务与验收标准，再批准当前版本。");label=Ui.T("查看并批准计划");destination="planning";}
    else{text=Ui.T("先确认项目目标与团队分工，再让成员讨论出一份计划。");label=Ui.T("前往团队讨论");destination="planning";}
    Note(c,text,40);var a=Actions(c);a.Controls.Add(Button(label,()=>Navigate(destination),true));a.Controls.Add(Button(Ui.T("配置团队"),()=>Navigate("team"),false));
    int done=p.results.Values.Count(x=>x.status=="done");Note(c,Ui.T("团队  ")+p.team.Count+Ui.T(" 人 / ")+p.levels+Ui.T(" 层    ·    交付  ")+done+" / "+(p.plan==null?0:p.plan.tasks.Count)+Ui.T(" 项    ·    API 调用  ")+p.calls+" / "+p.maxCalls,34);
  }
  List<Choice> AgentChoices(Project p){return p.team.Select(a=>new Choice(a.id,a.name)).ToList();}
  List<Choice> ProviderChoices(){return new[]{new Choice("",Ui.T("未绑定（演示可留空）"))}.Concat(state.providers.Select(a=>new Choice(a.id,a.name+" / "+a.model))).ToList();}
  string ChoiceValue(ComboBox c){return c.SelectedItem is Choice?((Choice)c.SelectedItem).Value:"";}

  void RenderWelcome(){var c=Card(Ui.T("从一个目标，开始一场协作"));Note(c,Ui.T("独立提案 → 交叉讨论 → 用户修改或批准 → 并行执行 → 成果验收。\n这是独立 Windows 桌面程序，无需浏览器。网页版继续保留，并共享本机项目。"),75);var a=Actions(c);a.Controls.Add(Button(Ui.T("创建项目"),async()=>await Run(NewProject),true));a.Controls.Add(Button(Ui.T("配置 API"),()=>{section="providers";Render();},false));}
  async Task NewProject(){if(!LeaveEdits())return;using(var f=Dialog(Ui.T("新建项目"))){var c=DialogCard(f);var name=Field(c,Ui.T("项目名称"),Ui.T("新项目"),30);var objective=Field(c,Ui.T("项目目标"),"",120);bool ok=false;var buttons=Actions(c);buttons.Controls.Add(Button(Ui.T("创建"),()=>{if(String.IsNullOrWhiteSpace(name.Text)||String.IsNullOrWhiteSpace(objective.Text)){MessageBox.Show(f,Ui.T("请填写项目名称和目标。"));return;}ok=true;f.Close();},true));buttons.Controls.Add(Button(Ui.T("取消"),()=>f.Close(),false));f.ShowDialog(this);if(!ok)return;var response=await Post("projects/create",new{name=name.Text,goal=objective.Text,locale=Ui.English?"en":"zh"});selected=Convert.ToString(response["id"]);section="overview";dirty=false;}}
  void RenderOverview(Project p){
    NextStep(p);var c=Card(Ui.T("项目任务书"));
    if(!editingBrief){Note(c,Ui.T("项目目标"),28);Output(c,p.goal,100);if(!String.IsNullOrWhiteSpace(p.constraints)){Note(c,Ui.T("约束与验收要求"),28);Output(c,p.constraints,76);}var buttons=Actions(c);var edit=Button(Ui.T("编辑任务书"),()=>{editingBrief=true;Render();},false);edit.Enabled=!Active;buttons.Controls.Add(edit);buttons.Controls.Add(Button(Ui.T("导出项目"),Export,false));}
    else{projectName=Field(c,Ui.T("项目名称"),p.name,30);goal=Field(c,Ui.T("项目目标与交付物"),p.goal,105);constraints=Field(c,Ui.T("约束与验收要求"),p.constraints,80);mode=Select(c,Ui.T("运行方式"),new List<Choice>{new Choice("demo",Ui.T("离线演示（不调用 API）")),new Choice("live",Ui.T("真实 API 模式"))},p.mode);
      var advanced=Disclosure(c,Ui.T("并行与调用上限"),false);concurrency=Number(advanced,Ui.T("最大并行任务数"),p.concurrency,1,6);maxCalls=Number(advanced,Ui.T("累计 API 调用上限"),p.maxCalls,1,1000);
      Note(c,Ui.T("修改目标、约束或模式会清空旧计划与成果，并要求重新批准。"),42);saveCurrent=async()=>{await Post("projects/save",new{projectId=p.id,name=projectName.Text,goal=goal.Text,constraints=constraints.Text,mode=ChoiceValue(mode),concurrency=(int)concurrency.Value,maxCalls=(int)maxCalls.Value});dirty=false;editingBrief=false;};var a=Actions(c);a.Controls.Add(Button(Ui.T("保存修改  Ctrl+S"),async()=>await Run(saveCurrent),true));a.Controls.Add(Button(Ui.T("取消编辑"),()=>{if(LeaveEdits()){editingBrief=false;Render();}},false));}
    var log=Card(Ui.T("最近动态"));var history=Disclosure(log,Ui.T("查看项目活动记录"),false);Output(history,String.Join("\n",p.events.AsEnumerable().Reverse().Take(8).Select(x=>x.at+"  "+x.text)),170);
  }

  DataGridView Grid(FlowLayoutPanel c,int height){var g=new DataGridView{Width=760,Height=height,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BackgroundColor=Color.White,BorderStyle=BorderStyle.FixedSingle,RowHeadersWidth=28,AllowUserToAddRows=false,AllowUserToDeleteRows=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,EditMode=DataGridViewEditMode.EditOnEnter,AutoGenerateColumns=false};g.DefaultCellStyle.WrapMode=DataGridViewTriState.True;g.DefaultCellStyle.SelectionBackColor=Color.FromArgb(224,239,225);g.DefaultCellStyle.SelectionForeColor=ink;g.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(235,242,231);g.EnableHeadersVisualStyles=false;g.RowTemplate.Height=76;g.ColumnHeadersHeight=42;g.DefaultCellStyle.Padding=new Padding(6);g.DefaultCellStyle.BackColor=Color.White;g.AlternatingRowsDefaultCellStyle.BackColor=Color.FromArgb(248,250,252);g.GridColor=Color.FromArgb(226,232,240);g.CellValueChanged+=Changed;g.CellBeginEdit+=Changed;g.CurrentCellDirtyStateChanged+=(s,e)=>{if(g.IsCurrentCellDirty)g.CommitEdit(DataGridViewDataErrorContexts.Commit);};g.DataError+=(s,e)=>{e.ThrowException=false;};c.Controls.Add(g);return g;}
  void TextColumn(DataGridView g,string name,string label,int weight,bool readOnly){var col=new DataGridViewTextBoxColumn{Name=name,HeaderText=label,FillWeight=weight,MinimumWidth=name=="level"||name=="id"?52:120,ReadOnly=readOnly};g.Columns.Add(col);}
  void ComboColumn(DataGridView g,string name,string label,List<Choice> choices,int weight){var col=new DataGridViewComboBoxColumn{Name=name,HeaderText=label,FillWeight=weight,MinimumWidth=130,DisplayStyle=DataGridViewComboBoxDisplayStyle.DropDownButton};foreach(var choice in choices)col.Items.Add(choice);col.DisplayMember="Label";col.ValueMember="Value";g.Columns.Add(col);}
  string Cell(DataGridViewRow r,string name){var v=r.Cells[name].Value;return v is Choice?((Choice)v).Value:Convert.ToString(v)??"";}
  int ReadLevel(string value){int level;if(!Int32.TryParse(value,out level)||level<1||level>12)throw new Exception(Ui.T("成员层级请输入 1–12 的整数。"));return level;}
  void RenderTeamTable(Project p){var c=Card(Ui.T("项目组织与岗位"));levels=Number(c,Ui.T("组织层级数（推荐 3 或 4 层，可设 1–12 层）"),p.levels,1,12);Note(c,Ui.T("人类用户不计入 AI 层级。层级编号越小，位置越高；负责人必须在更高层。\n首位最高层成员汇总计划；最后一位成员提供最终审核意见。可在表格中直接修改。"),65);
    teamGrid=Grid(c,320);TextColumn(teamGrid,"id","ID",10,true);teamGrid.Columns["id"].Visible=false;TextColumn(teamGrid,"name",Ui.T("成员名称"),20,false);TextColumn(teamGrid,"role",Ui.T("岗位职责"),35,false);TextColumn(teamGrid,"department",Ui.T("职能部门"),22,false);TextColumn(teamGrid,"level",Ui.T("层级"),8,false);ComboColumn(teamGrid,"managerId",Ui.T("直属负责人"),new[]{new Choice("",Ui.T("向用户汇报"))}.Concat(AgentChoices(p)).ToList(),22);ComboColumn(teamGrid,"providerId",Ui.T("API 连接"),ProviderChoices(),30);
    teamGrid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None;
    var columnWidths=new Dictionary<string,int>{{"name",160},{"role",260},{"department",160},{"level",76},{"managerId",190},{"providerId",220}};
    foreach(var pair in columnWidths){teamGrid.Columns[pair.Key].MinimumWidth=Dpx(pair.Value);teamGrid.Columns[pair.Key].Width=Dpx(pair.Value);}
    teamGrid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.AutoSize;
    teamGrid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;
    teamGrid.RowTemplate.MinimumHeight=Dpx(64);teamGrid.ScrollBars=ScrollBars.Both;
    foreach(var a in p.team)teamGrid.Rows.Add(a.id,a.name,a.role,a.department??"",a.level,a.managerId??"",a.providerId??"");
    teamGrid.Columns["name"].Frozen=true;
    Note(c,Ui.T("↔ 横向滚动查看负责人和 API 连接；成员名称固定在左侧。修改完成后点击“保存团队”。"),48);
    teamGrid.Enabled=!Active;var actions=Actions(c);var add=Button(Ui.T("添加成员"),()=>{teamGrid.Rows.Add(Guid.NewGuid().ToString(),Ui.T("新成员"),Ui.T("执行专项任务"),"",(int)levels.Value,"","");dirty=true;},false);add.Enabled=!Active;actions.Controls.Add(add);var remove=Button(Ui.T("移除选中成员"),()=>{if(teamGrid.CurrentRow!=null){teamGrid.Rows.Remove(teamGrid.CurrentRow);dirty=true;}},false);remove.Enabled=!Active;actions.Controls.Add(remove);saveCurrent=async()=>{teamGrid.EndEdit();var team=teamGrid.Rows.Cast<DataGridViewRow>().Select(r=>new Agent{id=Cell(r,"id"),name=Cell(r,"name"),role=Cell(r,"role"),department=Cell(r,"department").Trim(),level=ReadLevel(Cell(r,"level")),managerId=Cell(r,"managerId"),providerId=Cell(r,"providerId")}).ToList();await Post("projects/save",new{projectId=p.id,levels=(int)levels.Value,team=team});dirty=false;};var save=Button(Ui.T("保存团队  Ctrl+S"),async()=>await Run(saveCurrent),true);save.Enabled=!Active;actions.Controls.Add(save);
  }
  void RenderPlanning(Project p){var c=Card(Ui.T("多 AI 讨论，形成供你决策的计划"));Note(c,Ui.T("第一轮独立提案，第二轮交叉评议，再汇总成可编辑计划。未经你批准，不会执行任务。")+(p.mode=="demo"?Ui.T("\n当前为演示模式，下面的内容是流程示例。"):""),65);var a=Actions(c);if(Active)a.Controls.Add(Button(Ui.T("暂停运行"),async()=>await Run(()=>Post("projects/pause",new{projectId=p.id})),false));else a.Controls.Add(Button(p.discussion.Count>0?Ui.T("重新讨论"):Ui.T("开始团队讨论"),async()=>await Run(async()=>{if(!LeaveEdits())return;if(p.plan!=null&&MessageBox.Show(this,Ui.T("重新讨论会清空旧计划、批准与成果，继续？"),Ui.T("重新讨论"),MessageBoxButtons.YesNo)!=DialogResult.Yes)return;await Post("projects/discuss",new{projectId=p.id});}),true));
    if(p.discussion.Count>0){foreach(var message in p.discussion.OrderBy(m=>m.round)){var details=Disclosure(c,Ui.T("第 ")+message.round+Ui.T(" 轮 · ")+message.agentName+" / "+message.role,false);Output(details,message.text,220,true);}}
    if(p.plan==null){if(!Active){var b=Actions(c);b.Controls.Add(Button(Ui.T("手动创建计划"),async()=>await Run(()=>Post("projects/plan",new{projectId=p.id,plan=new Plan{summary=Ui.T("请编辑计划"),risks="",tasks=new List<WorkTask>{new WorkTask{id="T1",title=Ui.T("首个交付任务"),description=Ui.T("填写具体要求"),acceptance=Ui.T("填写验收标准"),agentId=p.team[0].id,dependsOn=new List<string>()}}}})),false));}return;}
    var planCard=Card(Ui.T("项目计划 · v")+p.revision);summary=Field(planCard,Ui.T("计划摘要"),p.plan.summary,85);risks=Field(planCard,Ui.T("风险与待确认事项"),p.plan.risks,65);taskGrid=Grid(planCard,330);TextColumn(taskGrid,"id","ID",8,true);TextColumn(taskGrid,"title",Ui.T("任务标题"),22,false);TextColumn(taskGrid,"description",Ui.T("任务说明"),30,false);ComboColumn(taskGrid,"agentId",Ui.T("负责人"),AgentChoices(p),18);TextColumn(taskGrid,"acceptance",Ui.T("验收标准"),26,false);TextColumn(taskGrid,"dependsOn",Ui.T("依赖 ID（逗号分隔）"),15,false);
    foreach(var t in p.plan.tasks)taskGrid.Rows.Add(t.id,t.title,t.description,t.agentId,t.acceptance,String.Join(",",t.dependsOn));taskGrid.Enabled=!Active;
    Note(planCard,Ui.T("保存修改会撤销旧批准并清空旧成果。批准后还需点击“启动执行”。"),38);var controls=Actions(planCard);
    var add=Button(Ui.T("添加任务"),()=>{int n=1;while(taskGrid.Rows.Cast<DataGridViewRow>().Any(r=>Cell(r,"id")=="T"+n))n++;taskGrid.Rows.Add("T"+n,Ui.T("新任务"),"",p.team[0].id,"","");dirty=true;},false);add.Enabled=!Active;controls.Controls.Add(add);
    var remove=Button(Ui.T("移除选中任务"),()=>{if(taskGrid.CurrentRow!=null){taskGrid.Rows.Remove(taskGrid.CurrentRow);dirty=true;}},false);remove.Enabled=!Active;controls.Controls.Add(remove);
    saveCurrent=async()=>{if(p.results.Count>0&&MessageBox.Show(this,Ui.T("保存将清空旧成果，继续？"),Ui.T("修改计划"),MessageBoxButtons.YesNo)!=DialogResult.Yes)return;await Post("projects/plan",new{projectId=p.id,plan=ReadPlan()});dirty=false;};var save=Button(Ui.T("保存计划  Ctrl+S"),async()=>await Run(saveCurrent),false);save.Enabled=!Active;controls.Controls.Add(save);
    var approve=Button(p.approval!=null?Ui.T("当前版本已批准"):Ui.T("批准当前计划"),async()=>await Run(async()=>{RequireSaved();await Post("projects/approve",new{projectId=p.id});}),true);approve.Enabled=!Active&&p.approval==null;controls.Controls.Add(approve);
    var execute=Button(Ui.T("启动执行"),async()=>await Run(async()=>{RequireSaved();await Post("projects/execute",new{projectId=p.id});section="results";}),true);execute.Enabled=!Active&&p.approval!=null;controls.Controls.Add(execute);
  }
  Plan ReadPlan(){taskGrid.EndEdit();return new Plan{summary=summary.Text,risks=risks.Text,tasks=taskGrid.Rows.Cast<DataGridViewRow>().Select(r=>new WorkTask{id=Cell(r,"id"),title=Cell(r,"title"),description=Cell(r,"description"),agentId=Cell(r,"agentId"),acceptance=Cell(r,"acceptance"),dependsOn=Cell(r,"dependsOn").Split(new[]{',','，'},StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim()).Where(x=>x.Length>0).ToList()}).ToList()};}
  void RequireSaved(){if(dirty)throw new Exception(Ui.T("请先保存当前页面的修改，再批准或执行。"));}
  void RenderResults(Project p){var c=Card(Ui.T("任务执行与成果"));var done=p.results.Values.Count(x=>x.status=="done");Note(c,done+" / "+(p.plan==null?0:p.plan.tasks.Count)+Ui.T(" 项任务已交付；最多并行 ")+p.concurrency+Ui.T(" 项。\n本版产出文本，不执行浏览、代码运行或外部发布。"),60);var progress=new ProgressBar{Minimum=0,Maximum=Math.Max(1,p.plan==null?0:p.plan.tasks.Count),Value=done,Height=12,Width=760,Style=ProgressBarStyle.Continuous,Margin=new Padding(0,8,0,12)};c.Controls.Add(progress);var a=Actions(c);if(Active)a.Controls.Add(Button(Ui.T("暂停执行"),async()=>await Run(()=>Post("projects/pause",new{projectId=p.id})),false));else{var run=Button(Ui.T("启动 / 继续执行"),async()=>await Run(()=>Post("projects/execute",new{projectId=p.id})),true);run.Enabled=p.approval!=null&&p.status!="delivered"&&p.status!="completed";a.Controls.Add(run);}a.Controls.Add(Button(Ui.T("导出项目"),Export,false));
    if(p.plan!=null)foreach(var t in p.plan.tasks){Result r;p.results.TryGetValue(t.id,out r);var task=Card(t.id+" · "+t.title+"    "+(r==null?Ui.T("等待执行"):r.status=="done"?Ui.T("已提交"):r.status=="running"?Ui.T("执行中"):Ui.T("需重试")));Note(task,Ui.T("负责人：")+p.team.First(x=>x.id==t.agentId).name+Ui.T("  /  依赖：")+(t.dependsOn.Count==0?Ui.T("无"):String.Join(", ",t.dependsOn))+Ui.T("\n验收：")+t.acceptance,65);if(r!=null)Output(task,r.text??r.error??Ui.T("处理中…"),180,true);}
    if(!String.IsNullOrEmpty(p.review)){var review=Card(Ui.T("最终审核意见"));Output(review,p.review,180,true);var accept=Button(p.status=="completed"?Ui.T("用户已验收"):Ui.T("确认验收完成"),async()=>await Run(()=>Post("projects/accept",new{projectId=p.id})),true);accept.Enabled=p.status=="delivered";Actions(review).Controls.Add(accept);}
  }
  void RenderProviders(){var c=Card(Ui.T("模型厂商不固定，按 API 协议接入"));Note(c,Ui.T("密钥在本机加密保存，不从服务返回桌面界面。真实运行会向所配置的接口发送相关项目内容。\n支持同步 HTTP JSON 文本接口；OAuth、复杂签名、流式或工具调用需要后续适配。"),65);Actions(c).Controls.Add(Button(Ui.T("添加 API 连接"),async()=>await Run(()=>EditProvider(null)),true));
    foreach(var p in state.providers){var card=Card(p.name+"  /  "+p.model);Note(card,p.endpoint+"\n"+p.protocol+" · "+(p.hasKey?Ui.T("已保存密钥"):Ui.T("未配置密钥")),50);var a=Actions(card);a.Controls.Add(Button(Ui.T("编辑连接"),async()=>await Run(()=>EditProvider(p)),false));a.Controls.Add(Button(Ui.T("测试连接"),async()=>await Run(async()=>{var r=await Post("providers/test",new{id=p.id});MessageBox.Show(this,Convert.ToString(r["message"])+"\n"+Convert.ToString(r["text"]),Ui.T("连接测试"));}),false));a.Controls.Add(Button(Ui.T("删除连接"),async()=>await Run(async()=>{if(MessageBox.Show(this,Ui.T("删除这个 API 连接？"),Ui.T("删除连接"),MessageBoxButtons.YesNo)==DialogResult.Yes)await Post("providers/delete",new{id=p.id});}),false));}
  }
  Form Dialog(string text){var f=new Form{Text=text,Size=new Size(790,740),MinimumSize=new Size(620,550),StartPosition=FormStartPosition.CenterParent,Font=Font,AutoScaleMode=AutoScaleMode.None,BackColor=paper,MinimizeBox=false,MaximizeBox=false};f.Load+=(s,e)=>{scaling=true;f.Scale(new SizeF(uiScale,uiScale));scaling=false;var area=Screen.FromControl(this).WorkingArea;f.Size=new Size(Math.Min(f.Width,area.Width-40),Math.Min(f.Height,area.Height-60));foreach(Control child in f.Controls)FitCard(child);};f.Shown+=(s,e)=>{var area=Screen.FromControl(this).WorkingArea;f.Location=new Point(Math.Max(area.Left,Math.Min(Left+(Width-f.Width)/2,area.Right-f.Width)),Math.Max(area.Top,Math.Min(Top+(Height-f.Height)/2,area.Bottom-f.Height)));};return f;}
  FlowLayoutPanel DialogCard(Form f){var c=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(24)};f.Controls.Add(c);c.SizeChanged+=(s,e)=>{if(!scaling)FitCard(c);};return c;}
  async Task EditProvider(Provider p){using(var f=Dialog(p==null?Ui.T("添加 API 连接"):Ui.T("编辑 API 连接"))){rendering=true;var c=DialogCard(f);var name=Field(c,Ui.T("连接名称"),p==null?"":p.name,30);var model=Field(c,Ui.T("模型 ID"),p==null?"":p.model,30);var endpoint=Field(c,Ui.T("完整 API 请求地址（包含接口路径）"),p==null?"":p.endpoint,30);var protocol=Select(c,Ui.T("接口协议"),new List<Choice>{new Choice("chat",Ui.T("Chat Completions 兼容")),new Choice("anthropic","Anthropic Messages"),new Choice("custom",Ui.T("自定义 JSON"))},p==null?"chat":p.protocol);var key=Field(c,Ui.T("API 密钥（编辑时留空保留原密钥）"),"",30);key.UseSystemPasswordChar=true;
      var custom=new FlowLayoutPanel{FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoSize=true,Width=760};c.Controls.Add(custom);custom.SizeChanged+=(s,e)=>{if(!scaling)FitCard(custom);};
      var body=Field(custom,Ui.T("自定义请求体模板（仅自定义 JSON 使用）"),p==null?"{\"model\":\"{{model}}\",\"messages\":\"{{messages}}\",\"stream\":false}":p.bodyTemplate,70);var headers=Field(custom,Ui.T("自定义请求头模板（使用 {{apiKey}}，不要硬编码密钥）"),p==null?"{\"Authorization\":\"Bearer {{apiKey}}\"}":p.headerTemplate,60);var response=Field(custom,Ui.T("响应文本路径"),p==null?"choices.0.message.content":p.responsePath,30);custom.Visible=ChoiceValue(protocol)=="custom";protocol.SelectedIndexChanged+=(s,e)=>{custom.Visible=ChoiceValue(protocol)=="custom";};bool ok=false;var actions=Actions(c);actions.Controls.Add(Button(Ui.T("保存连接"),()=>{ok=true;f.Close();},true));actions.Controls.Add(Button(Ui.T("取消"),()=>f.Close(),false));f.ShowDialog(this);rendering=false;if(!ok)return;await Post("providers/save",new{id=p==null?null:p.id,name=name.Text,model=model.Text,endpoint=endpoint.Text,protocol=ChoiceValue(protocol),apiKey=key.Text,bodyTemplate=body.Text,headerTemplate=headers.Text,responsePath=response.Text});dirty=false;}}
  void Export(){var p=Current;if(p==null)return;using(var dialog=new SaveFileDialog{Filter=Ui.T("Markdown 文档 (*.md)|*.md"),FileName=String.Join("_",p.name.Split(Path.GetInvalidFileNameChars()))+".md"}){if(dialog.ShowDialog(this)!=DialogResult.OK)return;var b=new StringBuilder();b.AppendLine("# "+p.name).AppendLine(Ui.T("\n模式：")+p.mode).AppendLine(Ui.T("\n## 项目目标\n")+p.goal).AppendLine(Ui.T("\n## 项目计划\n")+(p.plan==null?Ui.T("暂无"):json.Serialize(p.plan)));foreach(var m in p.discussion)b.AppendLine("\n## "+m.agentName+Ui.T(" · 第 ")+m.round+Ui.T(" 轮\n")+m.text);foreach(var r in p.results)b.AppendLine(Ui.T("\n## 任务 ")+r.Key+"\n"+r.Value.text);b.AppendLine(Ui.T("\n## 审核意见\n")+p.review);File.WriteAllText(dialog.FileName,b.ToString(),new UTF8Encoding(true));MessageBox.Show(this,Ui.T("已保存到：\n")+dialog.FileName,Ui.T("导出成功"));}}

  public static void SelfTest(){using(var f=new MainForm()){f.timer.Stop();var p=new Project{id="p",name=Ui.T("桌面自检"),goal=Ui.T("测试"),constraints="",mode="demo",status="awaiting_approval",levels=3,concurrency=3,maxCalls=80,team=new List<Agent>{new Agent{id="a",department="Planning",name=Ui.T("负责人"),role=Ui.T("协调"),level=1,providerId="",managerId=""},new Agent{id="b",department="Quality",name=Ui.T("执行员"),role=Ui.T("执行"),level=3,providerId="",managerId="a"}},plan=new Plan{summary=Ui.T("计划"),risks="",tasks=new List<WorkTask>{new WorkTask{id="T1",title=Ui.T("任务"),description=Ui.T("说明"),agentId="a",acceptance=Ui.T("标准"),dependsOn=new List<string>()}}},discussion=new List<Discussion>{new Discussion{agentName=Ui.T("负责人"),round=1,text=Ui.T("提案")}},results=new Dictionary<string,Result>{{"T1",new Result{status="done",text=Ui.T("成果")}}},commands=new List<ProjectCommand>{new ProjectCommand{text="Hello team",status="done",replies=new List<CommandReply>{new CommandReply{agentId="a",agentName="Lead",text="Reply visible"}}}},events=new List<Event>(),review=Ui.T("审核意见")};f.state.projects.Add(p);f.selected=p.id;foreach(var section in new[]{"overview","planning","team","results","providers","details"}){f.section=section;f.teamTable=section=="team";f.Render();if(section!="details"&&f.content.Controls.Count==0)throw new Exception("Empty native page: "+section);if(section=="details")f.TestDetailsWorkspace(p);if(section=="planning"){var plan=f.ReadPlan();if(plan.tasks[0].agentId!="a"||plan.summary!=Ui.T("计划"))throw new Exception("Plan binding failed");}if(section=="team")f.TestTeamLayout();if(section=="team"&&f.Cell(f.teamGrid.Rows[1],"managerId")!="a")throw new Exception("Manager binding failed");}f.section="planning";f.Render();var edited=f.summary;edited.Text=Ui.T("未保存的计划");f.Run(()=>Task.FromResult(0)).GetAwaiter().GetResult();if(f.summary!=edited||f.summary.Text!=Ui.T("未保存的计划")||!f.dirty)throw new Exception("Unsaved edit was lost after a cancelled operation");f.dirty=false;f.section="overview";f.editingBrief=true;f.Render();if(f.saveCurrent==null||f.goal.Text!=p.goal)throw new Exception("Brief save binding missing");using(var chart=new OrganizationChart(p,f.state.providers,1F)){if(chart.DepartmentCount!=2)throw new Exception("Functional department lanes missing");if(f.CopyTeam(p)[1].department!="Quality")throw new Exception("Department binding lost");if(chart.NodeCount!=p.team.Count)throw new Exception("Organization chart missing members");}var parsed=f.json.Deserialize<State>(f.json.Serialize(f.state));if(parsed.projects[0].plan.tasks.Count!=1)throw new Exception("JSON round-trip failed");}}
}
}
