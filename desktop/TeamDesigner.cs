using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Cofoundry {
public partial class MainForm {
  bool teamTable;

  void RenderTeam(Project p) {
    var header=Card(Ui.T("团队组织"));
    Note(header,Ui.T("按职能部门分栏，纵向显示职位层级。点击卡片编辑部门、岗位和模型；实线表示已配置的直属汇报关系。"),48);
    var actions=Actions(header);
    var add=Button(Ui.T("＋ 添加成员"),async()=>await Run(()=>EditMember(p,null,null)),true);add.Enabled=!Active;actions.Controls.Add(add);
    var depth=Button(Ui.T("设置层级 · ")+p.levels,async()=>await Run(()=>EditDepth(p)),false);depth.Enabled=!Active;actions.Controls.Add(depth);
    actions.Controls.Add(Button(teamTable?Ui.T("组织架构图"):Ui.T("表格批量编辑"),()=>{if(LeaveEdits()){teamTable=!teamTable;Render();}},false));
    if(teamTable){RenderTeamTable(p);return;}
    var chart=new OrganizationChart(p,state.providers,uiScale);
    chart.MemberSelected+=async agent=>await Run(()=>EditMember(p,agent,null));
    header.Controls.Add(chart);
    Note(header,Ui.T("横向或纵向滚动可查看大团队。修改团队会撤销旧计划与成果，运行期间只能查看。"),42);
  }

  List<Agent> CopyTeam(Project p){return p.team.Select(a=>new Agent{id=a.id,name=a.name,role=a.role,department=a.department,level=a.level,managerId=a.managerId,providerId=a.providerId}).ToList();}
  bool ConfirmTeamChange(Project p,Form owner){return p.plan==null||MessageBox.Show(owner,Ui.T("修改团队将清空当前计划、批准和成果。请先导出需要保留的内容。继续保存？"),Ui.T("修改团队"),MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes;}

  async Task EditDepth(Project p) {
    if(!LeaveEdits())return;
    using(var dialog=Dialog(Ui.T("设置组织层级"))) {
      var card=DialogCard(dialog);Note(card,Ui.T("推荐 3 层：负责人 → 专业负责人 → 执行成员。复杂项目可增加到 4 层，也可自定义。"),65);
      var value=Number(card,Ui.T("层级数"),p.levels,1,12);
      Actions(card).Controls.Add(Button(Ui.T("保存层级"),async()=>{
        if(p.team.Any(a=>a.level>(int)value.Value)){MessageBox.Show(dialog,Ui.T("仍有成员位于更深的层级，请先调整这些成员。"),Ui.T("无法减少层级"));return;}
        if(!ConfirmTeamChange(p,dialog))return;
        dialog.Enabled=false;
        try{await Post("projects/save",new{projectId=p.id,levels=(int)value.Value,team=p.team});dirty=false;dialog.Close();}
        catch(Exception ex){MessageBox.Show(dialog,ex.Message,Ui.T("保存失败"));}
        finally{if(!dialog.IsDisposed)dialog.Enabled=true;}
      },true));
      dialog.ShowDialog(this);
    }
    await Task.FromResult(0);
  }

  async Task EditMember(Project p,Agent member,Agent parent) {
    if(!LeaveEdits())return;
    using(var dialog=Dialog(member==null?Ui.T("添加团队成员"):Ui.T("编辑团队成员"))) {
      var card=DialogCard(dialog);
      var name=Field(card,Ui.T("成员名称"),member==null?"":member.name,30);
      var role=Field(card,Ui.T("岗位职责"),member==null?"":member.role,80);
      var department=Field(card,Ui.T("职能部门（如：研发、设计、质量）"),member!=null?member.department:parent!=null?parent.department:"",30);department.MaxLength=60;
      var level=Number(card,Ui.T("所属层级"),member!=null?member.level:parent!=null?Math.Min(parent.level+1,p.levels):p.levels,1,12);
      level.Maximum=p.levels;
      var manager=Select(card,Ui.T("直属负责人"),new List<Choice>{new Choice("",Ui.T("向用户汇报"))},"");
      string managerId=member!=null?member.managerId:parent!=null?parent.id:"";
      Action updateManagers=()=>{
        manager.Items.Clear();manager.Items.Add(new Choice("",Ui.T("向用户汇报")));
        foreach(var person in p.team.Where(a=>a.id!=(member==null?null:member.id)&&a.level<(int)level.Value))manager.Items.Add(new Choice(person.id,person.name));
        manager.SelectedIndex=0;
        for(int i=0;i<manager.Items.Count;i++)if(((Choice)manager.Items[i]).Value==managerId)manager.SelectedIndex=i;
      };
      updateManagers();level.ValueChanged+=(s,e)=>{managerId=ChoiceValue(manager);updateManagers();};
      var provider=Select(card,Ui.T("模型连接"),ProviderChoices(),member==null?"":member.providerId);
      Note(card,Ui.T("负责人必须处于更高层；演示模式可以不绑定模型。"),40);
      var actions=Actions(card);
      var save=Button(Ui.T("保存成员"),async()=>{
        if(String.IsNullOrWhiteSpace(name.Text)||String.IsNullOrWhiteSpace(role.Text)){MessageBox.Show(dialog,Ui.T("请填写成员名称和岗位职责。"),Ui.T("信息不完整"));return;}
        if(member!=null&&p.team.Any(a=>a.managerId==member.id&&a.level<=(int)level.Value)){MessageBox.Show(dialog,Ui.T("该成员仍有下属位于同层或更高层。请先调整下属层级。"),Ui.T("汇报关系无效"));return;}
        if(member==null&&p.team.Count>=12){MessageBox.Show(dialog,Ui.T("当前最多支持 12 位成员。"),Ui.T("团队人数已满"));return;}
        if(!ConfirmTeamChange(p,dialog))return;
        var team=CopyTeam(p);var edited=new Agent{id=member==null?Guid.NewGuid().ToString():member.id,name=name.Text.Trim(),role=role.Text.Trim(),department=department.Text.Trim(),level=(int)level.Value,managerId=ChoiceValue(manager),providerId=ChoiceValue(provider)};
        if(member==null)team.Add(edited);else team[team.FindIndex(a=>a.id==member.id)]=edited;
        dialog.Enabled=false;
        try{await Post("projects/save",new{projectId=p.id,levels=p.levels,team=team});dirty=false;dialog.Close();}
        catch(Exception ex){MessageBox.Show(dialog,ex.Message,Ui.T("保存失败"));}
        finally{if(!dialog.IsDisposed)dialog.Enabled=true;}
      },true);save.Enabled=!Active;actions.Controls.Add(save);
      if(member!=null){
        var child=Button(Ui.T("添加下属"),()=>{dialog.Tag="child";dialog.Close();},false);child.Enabled=!Active&&member.level<p.levels&&p.team.Count<12;actions.Controls.Add(child);
        var remove=Button(Ui.T("移除成员"),async()=>{
          if(p.team.Count<=2){MessageBox.Show(dialog,Ui.T("讨论团队至少保留两位成员。"),Ui.T("无法移除"));return;}
          string warning=Ui.T("移除该成员后，其直属下属将向该成员的负责人汇报。")+(p.plan!=null?Ui.T("当前计划、批准和成果也会清空。"):"")+Ui.T("继续？");
          if(MessageBox.Show(dialog,warning,Ui.T("移除成员"),MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
          var team=CopyTeam(p);team.RemoveAll(a=>a.id==member.id);foreach(var person in team.Where(a=>a.managerId==member.id))person.managerId=member.managerId;
          dialog.Enabled=false;try{await Post("projects/save",new{projectId=p.id,levels=p.levels,team=team});dirty=false;dialog.Close();}catch(Exception ex){MessageBox.Show(dialog,ex.Message,Ui.T("保存失败"));}finally{if(!dialog.IsDisposed)dialog.Enabled=true;}
        },false);remove.Enabled=!Active;actions.Controls.Add(remove);
      }
      actions.Controls.Add(Button(Ui.T("关闭"),()=>dialog.Close(),false));
      if(Active)foreach(Control control in new Control[]{name,role,department,level,manager,provider})control.Enabled=false;
      dialog.ShowDialog(this);
      if(Convert.ToString(dialog.Tag)=="child")await EditMember(p,null,member);
    }
  }
}

public class OrganizationChart : Panel {
  readonly Project project; readonly List<Provider> providers; readonly float scale;
  readonly Dictionary<string,Button> nodes=new Dictionary<string,Button>();
  readonly Dictionary<string,Label> departments=new Dictionary<string,Label>();
  string Department(Agent a){return String.IsNullOrWhiteSpace(a.department)?Ui.T("未设置职能部门"):a.department.Trim();}
  readonly Label human;
  public event Action<Agent> MemberSelected;
  protected override bool ScaleChildren {get{return false;}}
  int Px(int value){return (int)Math.Round(value*scale);}
  public OrganizationChart(Project p,List<Provider> connections,float displayScale) {
    project=p;providers=connections;scale=displayScale;DoubleBuffered=true;AutoScroll=true;
    BackColor=Color.FromArgb(248,250,252);Width=850;Height=540;Margin=new Padding(0,18,0,16);
    AccessibleName=Ui.T("团队组织架构图");
    human=new Label{Text=Ui.T("你 · 项目决策者\n批准计划 / 验收成果"),TextAlign=ContentAlignment.MiddleCenter,BackColor=Color.FromArgb(224,231,255),ForeColor=Color.FromArgb(67,56,202)};Controls.Add(human);
    foreach(var name in project.team.Select(Department).Distinct()){var label=new Label{Text=name,TextAlign=ContentAlignment.MiddleCenter,BackColor=Color.FromArgb(226,232,240),ForeColor=Color.FromArgb(51,65,85)};departments.Add(name,label);Controls.Add(label);}
    foreach(var a in project.team){var provider=providers.FirstOrDefault(v=>v.id==a.providerId);var b=new Button{Text="L"+a.level+"  "+a.name+"\n"+a.role+"\n"+(provider==null?Ui.T("未绑定模型"):provider.name),AccessibleName=a.name+Ui.T("，层级 ")+a.level,TextAlign=ContentAlignment.MiddleLeft,FlatStyle=FlatStyle.Flat,BackColor=Color.White,ForeColor=Color.FromArgb(30,41,59),Padding=new Padding(Px(14)),Cursor=Cursors.Hand,TabIndex=project.team.IndexOf(a)};b.FlatAppearance.BorderColor=Color.FromArgb(165,180,252);b.FlatAppearance.MouseOverBackColor=Color.FromArgb(238,242,255);b.Click+=(s,e)=>{if(MemberSelected!=null)MemberSelected(a);};nodes.Add(a.id,b);Controls.Add(b);}
    Resize+=(s,e)=>LayoutNodes();Scroll+=(s,e)=>Invalidate();LayoutNodes();
  }
  void LayoutNodes(){
    if(nodes.Count==0)return;
    var groups=project.team.GroupBy(Department).ToArray();
    int needed=groups.Sum(g=>g.GroupBy(a=>a.level).Max(l=>l.Count())*Px(252)+Px(24));
    int width=Math.Max(ClientSize.Width-Px(24),needed+Px(48));
    int height=Px(190+project.levels*182);AutoScrollMinSize=new Size(width,height);
    var offset=AutoScrollPosition;
    human.Bounds=new Rectangle(offset.X+(width-Px(240))/2,offset.Y+Px(20),Px(240),Px(66));
    int left=(width-needed)/2;
    foreach(var group in groups){int laneWidth=group.GroupBy(a=>a.level).Max(l=>l.Count())*Px(252)+Px(24);departments[group.Key].Bounds=new Rectangle(offset.X+left,offset.Y+Px(112),laneWidth-Px(12),Px(36));
      foreach(var level in group.GroupBy(a=>a.level)){var people=level.OrderBy(a=>a.managerId??"").ToArray();int start=left+(laneWidth-people.Length*Px(252))/2;for(int i=0;i<people.Length;i++)nodes[people[i].id].Bounds=new Rectangle(offset.X+start+i*Px(252),offset.Y+Px(176+(level.Key-1)*182),Px(234),Px(124));}
      left+=laneWidth;
    }
    Invalidate();
  }
  protected override void OnPaint(PaintEventArgs e){
    base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
    using(var pen=new Pen(Color.FromArgb(148,163,184),Math.Max(1,scale))){
      pen.CustomEndCap=new AdjustableArrowCap(3,4);
      foreach(var a in project.team){Control parent=String.IsNullOrEmpty(a.managerId)?(Control)human:nodes.ContainsKey(a.managerId)?(Control)nodes[a.managerId]:human;var child=nodes[a.id];int x1=parent.Left+parent.Width/2,y1=parent.Bottom,x2=child.Left+child.Width/2,y2=child.Top;int bend=y1+Px(20);e.Graphics.DrawLines(pen,new[]{new Point(x1,y1),new Point(x1,bend),new Point(x2,bend),new Point(x2,y2)});}
    }
    using(var brush=new SolidBrush(Color.FromArgb(100,116,139)))for(int level=1;level<=project.levels;level++)e.Graphics.DrawString("L"+level,Font,brush,AutoScrollPosition.X+Px(8),AutoScrollPosition.Y+Px(178+(level-1)*182));
  }
  public int NodeCount {get{return nodes.Count;}}
  public int DepartmentCount {get{return departments.Count;}}
}
}
