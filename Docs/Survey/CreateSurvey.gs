/**
 * JACKDEAD のプレイアンケートを Google フォームに作成するスクリプト。
 * https://script.google.com の新しいプロジェクトに貼り付けて、createJackdeadSurvey を一度だけ実行する。
 * （実行するたびに、フォームが新しく1つ作られる）
 */
function createJackdeadSurvey() {
  const form = FormApp.create('JACKDEAD プレイアンケート');
  form.setDescription('JACKDEAD を遊んでいただき、ありがとうございます。今後の改善のため、アンケートにご協力ください。');
  form.setCollectEmail(false); // メールアドレスは集めない
  form.setConfirmationMessage('ご回答ありがとうございました！');

  form.addScaleItem()
    .setTitle('ゲームの満足度を教えてください')
    .setBounds(1, 5)
    .setLabels('不満', '満足')
    .setRequired(true);

  form.addMultipleChoiceItem()
    .setTitle('プレイ中に、エラーや不具合の場面はありましたか？')
    .setChoiceValues(['あった', 'なかった'])
    .setRequired(true);

  form.addParagraphTextItem()
    .setTitle('「あった」と答えた方は、どんな場面だったか教えてください')
    .setHelpText('例：何をしていたときに、どうなったか');

  form.addParagraphTextItem()
    .setTitle('追加してほしいコンテンツがあれば教えてください');

  // 作ったままだと非公開の場合があるので、回答を受け付けられる状態にする
  if (typeof form.supportsAdvancedResponderPermissions === 'function' && form.supportsAdvancedResponderPermissions()) {
    form.setPublished(true);
  }

  Logger.log('回答用のURL: ' + form.getPublishedUrl());
  Logger.log('編集用のURL: ' + form.getEditUrl());
}
