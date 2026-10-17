using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Cofoundry {
public partial class MainForm {
  RichTextBox detailChat;
  Label detailProgress;
  string detailTranscript;
  string CommandStatus(string status){switch(status){case "queued":return Ui.T("排队中");case "running":return Ui.T("正在回复");case "done":return Ui.T("已回复");case "failed":return Ui.T("未完成");default:return status;}}
  void RefreshDetailsChat(Project p){
    if(detailChat==null||detailChat.IsDisposed)return;
    detailProgress.Text=Ui.T("当前计划 · v")+p.revision+"    "+p.results.Values.Count(r=>r.status=="done")+" / "+(p.plan==null?0:p.plan.tasks.Count)+Ui.T(" 项任务已提交");
    string snapshot=json.Serialize(p.commands);
    if(snapshot==detailTranscript)return;
    detailTranscript=snapshot;
    int caret=detailChat.SelectionStart;bool atEnd=caret>=detailChat.TextLength-1;
    detailChat.Clear();
    Action<string,string,Color> message=(who,body,color)=>{
      detailChat.SelectionColor=color;
      detailChat.AppendText(who+Environment.NewLine);
      detailChat.SelectionColor=ink;
      detailChat.AppendText((body??"").Replace("\r\n","\n").Replace("\n",Environment.NewLine)+Environment.NewLine+Environment.NewLine);
    };
    var commands=p.commands??new List<ProjectCommand>();
    if(commands.Count==0)message(Ui.T("项目对话"),Ui.T("向团队提问、补充要求或指定成员处理问题。回复会保存在这里。"),Color.DimGray);
    foreach(var item in commands){
      var recipient=p.team.FirstOrDefault(a=>a.id==item.targetId);
      string name=String.IsNullOrEmpty(item.targetId)?Ui.T("整个团队"):recipient==null?item.targetId:recipient.name;
      DateTime time;string stamp=DateTime.TryParse(item.at,out time)?time.ToLocalTime().ToString("MM-dd HH:mm"):item.at;
      message(Ui.T("你")+" → "+name+"  ·  "+stamp,item.text,green);
      foreach(var reply in item.replies??new List<CommandReply>())message(reply.agentName,reply.text,Color.FromArgb(15,118,110));
      message(CommandStatus(item.status),item.error??(item.status=="queued"?Ui.T("等待当前运行完成后回复。"):item.status=="running"?Ui.T("模型正在处理，请稍候…"):""),Color.DimGray);
    }
    detailChat.Select(atEnd?detailChat.TextLength:Math.Min(caret,detailChat.TextLength),0);detailChat.ScrollToCaret();
  }
  void RenderDetails(Project p){
    var overview=Card(Ui.T("项目详情"));
    detailProgress=Note(overview,"",32);
    var plan=Disclosure(overview,Ui.T("查看当前计划与任务进展"),false);
    Output(plan,p.goal+"\n\n"+(p.plan==null?Ui.T("暂无计划"):p.plan.summary),90,true);
    if(p.plan!=null)foreach(var task in p.plan.tasks){Result result;p.results.TryGetValue(task.id,out result);var row=Disclosure(plan,task.id+" · "+task.title+" · "+(result==null?Ui.T("等待执行"):result.status=="done"?Ui.T("已提交"):result.status=="running"?Ui.T("执行中"):Ui.T("需重试")),false);Output(row,task.description+"\n"+task.acceptance+"\n\n"+(result==null?"":result.text??result.error),150,true);}
    var conversation=Card(Ui.T("项目对话"));
    detailChat=new RichTextBox{ReadOnly=true,DetectUrls=false,Height=240,Width=760,BackColor=Color.FromArgb(248,250,252),BorderStyle=BorderStyle.None,Font=Font,ScrollBars=RichTextBoxScrollBars.Vertical,Margin=new Padding(0,0,0,12)};
    conversation.Controls.Add(detailChat);detailTranscript=null;RefreshDetailsChat(p);
    var target=Select(conversation,Ui.T("发送给"),new[]{new Choice("",Ui.T("整个团队"))}.Concat(AgentChoices(p)).ToList(),"");
    var input=Field(conversation,Ui.T("消息或新指令"),"",76);
    input.MaxLength=8000;
    var actions=Actions(conversation);
    var send=Button(Ui.T("发送  Ctrl+Enter"),async()=>await Run(async()=>{
      if(String.IsNullOrWhiteSpace(input.Text))throw new Exception(Ui.T("请输入指令内容。"));
      await Post("projects/command",new{projectId=p.id,targetId=ChoiceValue(target),text=input.Text});dirty=false;
    }),true);actions.Controls.Add(send);
    input.KeyDown+=(s,e)=>{if(e.Control&&e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;if(!busy)send.PerformClick();}};
    actions.Controls.Add(Button(Ui.T("暂停项目与回复"),async()=>await Run(()=>Post("projects/pause",new{projectId=p.id})),false));
    Note(conversation,Ui.T("运行中可以继续发送。指令先排队，后续调用会参考相关要求；范围变更仍需修改计划并重新批准。"),52);
    var activity=Disclosure(conversation,Ui.T("最近动态"),false);Output(activity,String.Join("\n",p.events.AsEnumerable().Reverse().Take(20).Select(e=>e.at+"  "+e.text)),180);
  }
}
}

