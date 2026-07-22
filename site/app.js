const state = document.querySelector('#state');
const panel = document.querySelector('#prediction');
const refreshButton = document.querySelector('#refresh');

function text(id, value) {
  document.querySelector(`#${id}`).textContent = value;
}

function renderTokens(id, values, format = value => value) {
  const parent = document.querySelector(`#${id}`);
  parent.replaceChildren(...values.map(value => {
    const item = document.createElement('span');
    item.className = 'token';
    item.textContent = format(value);
    return item;
  }));
}

function tokenGroup(values, className = '') {
  const group = document.createElement('div');
  group.className = `tokens ${className}`.trim();
  group.replaceChildren(...values.map(value => {
    const item = document.createElement('span');
    item.className = 'token';
    item.textContent = value;
    return item;
  }));
  return group;
}

function renderAiPeriods(results) {
  const parent = document.querySelector('#ai-periods');
  const periods = ['50', '100', '200', '500'];
  parent.replaceChildren(...periods.map(period => {
    const result = results?.[period];
    if (!result) throw new Error(`缺少 ${period} 期AI预测`);
    const block = document.createElement('article');
    block.className = 'period-result';
    const heading = document.createElement('h3');
    heading.textContent = `${period}期`;
    const meta = document.createElement('p');
    meta.className = 'period-meta';
    meta.textContent = `${result.confidence || '未标注可信度'} · ${result.best_model || '综合模型'}`;
    const label3 = document.createElement('b');
    label3.textContent = '重点3肖';
    const label6 = document.createElement('b');
    label6.textContent = '参考6肖';
    block.append(heading, meta, label3, tokenGroup(result.top3), label6,
      tokenGroup(result.top6, 'secondary compact'));
    return block;
  }));
}

function renderRanking(id, values, scoreKey) {
  const parent = document.querySelector(`#${id}`);
  parent.replaceChildren(...values.map((item, index) => {
    const row = document.createElement('div');
    row.className = 'rank-row';
    const rank = document.createElement('strong');
    rank.className = 'rank';
    rank.textContent = String(index + 1);
    const zodiac = document.createElement('b');
    zodiac.textContent = item.zodiac;
    const detail = document.createElement('span');
    detail.textContent = item.numbers || item.confidence || '';
    const score = document.createElement('strong');
    score.className = 'score';
    const raw = item[scoreKey];
    score.textContent = scoreKey === 'final_score' ? Number(raw).toFixed(3) : `${raw}分`;
    row.append(rank, zodiac, detail, score);
    return row;
  }));
}

async function fetchJson(path) {
  const response = await fetch(`${path}?v=${Date.now()}`, { cache: 'no-store' });
  if (!response.ok) throw new Error(`${path} 返回 HTTP ${response.status}`);
  return response.json();
}

async function loadPrediction() {
  refreshButton.disabled = true;
  state.className = 'state';
  state.textContent = '正在读取最新预测...';
  state.hidden = false;
  panel.hidden = true;
  try {
    const latest = await fetchJson('data/daily-records/latest.json');
    if (latest.status === 'generating') {
      state.textContent = '预测正在生成，请稍后刷新。';
      return;
    }
    if (latest.status === 'failed') throw new Error('生成失败，请查看 GitHub Actions 运行日志');
    if (!latest.prediction_file) throw new Error('latest.json 缺少 prediction_file');
    const result = await fetchJson(`data/daily-records/${latest.prediction_file}`);
    if (result.status !== 'success' || !result.validation?.passed) {
      throw new Error('最新预测文件状态无效');
    }

    text('issue-title', `第 ${result.issue} 期预测`);
    text('generated-at', new Date(result.generated_at).toLocaleString('zh-CN'));
    text('status', '生成成功');
    text('source-issue', `第 ${result.source_issue} 期`);
    renderAiPeriods(result.ai_zodiac);
    const rule = result.special_rule;
    text('rule-formula', `源期 ${rule.source_issue}：${String(rule.first_number).padStart(2, '0')} 与 ${String(rule.last_number).padStart(2, '0')} 尾数和 ${rule.tail_sum} → ${String(rule.mapped_number).padStart(2, '0')} → ${rule.mapped_zodiac}`);
    renderTokens('rule-zodiacs', rule.zodiacs);
    renderRanking('score-results', result.comprehensive_score, 'total_score');
    renderRanking('ensemble-results', result.ensemble, 'final_score');
    state.hidden = true;
    panel.hidden = false;
  } catch (error) {
    state.className = 'state error';
    state.textContent = `加载失败：${error.message}`;
  } finally {
    refreshButton.disabled = false;
  }
}

refreshButton.addEventListener('click', loadPrediction);
loadPrediction();
